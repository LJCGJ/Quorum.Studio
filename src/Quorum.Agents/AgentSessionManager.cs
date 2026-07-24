using System.Collections.Concurrent;
using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Core.Routing;

namespace Quorum.Agents;

/// <summary>
/// Executa varias sessoes ao mesmo tempo — opcao do usuario, nao obrigacao.
/// Enquanto um agente manipula um banco via MCP, outro pode conversar ou revisar
/// um resultado; sao tarefas independentes, cada uma com seu modelo e seu custo.
///
/// LIMITE DE SIMULTANEIDADE: o gargalo real nao e processamento, e memoria e
/// cota. Cada agente de tela sobe um navegador (centenas de MB) e cada provedor
/// tem limite de requisicoes por minuto. Por isso o teto padrao e conservador; o
/// usuario pode aumenta-lo sabendo o custo.
/// </summary>
public sealed class AgentSessionManager
{
    private readonly ConcurrentDictionary<Guid, AgentSession> _sessoes = new();
    private readonly SemaphoreSlim _vagas;
    private readonly AgentLoopOptions _options;

    /// <param name="maxSimultaneas">
    /// Quantas sessoes podem executar ao mesmo tempo. As demais aguardam vaga em
    /// vez de disputar memoria. Padrao 3.
    /// </param>
    public AgentSessionManager(int maxSimultaneas = 3)
    {
        if (maxSimultaneas < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSimultaneas),
                "E preciso permitir ao menos uma sessao por vez.");

        MaxConcurrent = maxSimultaneas;
        _vagas = new SemaphoreSlim(maxSimultaneas, maxSimultaneas);
        _options = AgentLoopOptions.Default;
    }

    public int MaxConcurrent { get; }

    /// <summary>Todas as sessoes, ativas e concluidas, da mais recente para a mais antiga.</summary>
    public IReadOnlyList<AgentSession> Sessions =>
        _sessoes.Values.OrderByDescending(s => s.StartedAt).ToArray();

    /// <summary>Quantas estao ocupando recursos agora.</summary>
    public int ActiveCount => _sessoes.Values.Count(s => s.IsActive);

    /// <summary>Avisada quando uma sessao e criada ou removida.</summary>
    public event Action? SessionsChanged;

    /// <summary>
    /// Cria e dispara uma sessao. Retorna imediatamente: quem chama acompanha pelo
    /// objeto devolvido (status, progresso, resultado), sem bloquear a interface.
    /// </summary>
    /// <param name="title">Nome curto da tarefa.</param>
    /// <param name="objective">O que o usuario pediu.</param>
    /// <param name="agentFactory">
    /// Cria o agente. Recebe o callback de progresso e o cancelamento — e uma
    /// funcao porque subir um servidor MCP e assincrono e pode falhar.
    /// </param>
    /// <param name="chain">Cadeia de modelos do roteador (ideal primeiro).</param>
    /// <param name="providerFactory">Cria o provider para um modelo.</param>
    /// <param name="options">Limites do loop.</param>
    public AgentSession Start(
        string title,
        string objective,
        Func<Action<string>, CancellationToken, Task<IQuorumAgent>> agentFactory,
        IReadOnlyList<ModelInfo> chain,
        Func<string, IAiProvider> providerFactory,
        AgentLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(providerFactory);

        var sessao = new AgentSession(title, objective);
        _sessoes[sessao.Id] = sessao;
        SessionsChanged?.Invoke();

        // Roda em segundo plano; a interface continua respondendo.
        _ = ExecutarAsync(sessao, agentFactory, chain, providerFactory, options);

        return sessao;
    }

    private async Task ExecutarAsync(
        AgentSession sessao,
        Func<Action<string>, CancellationToken, Task<IQuorumAgent>> agentFactory,
        IReadOnlyList<ModelInfo> chain,
        Func<string, IAiProvider> providerFactory,
        AgentLoopOptions? options)
    {
        var vagaTomada = false;
        try
        {
            if (chain.Count == 0)
            {
                sessao.Fail("Nenhum modelo disponivel para esta tarefa. " +
                            "Verifique a chave de API e a lista de modelos.");
                return;
            }

            // Espera vaga: evita subir cinco navegadores de uma vez.
            sessao.SetStatus(SessionStatus.Starting);
            if (_vagas.CurrentCount == 0)
                sessao.Report(">>> Aguardando vaga (limite de tarefas simultaneas atingido)...");

            await _vagas.WaitAsync(sessao.Token).ConfigureAwait(false);
            vagaTomada = true;

            sessao.Report(">>> Preparando o agente...");
            await using var agente = await agentFactory(sessao.Report, sessao.Token)
                .ConfigureAwait(false);

            sessao.SetModel(chain[0].Id);
            sessao.SetStatus(SessionStatus.Running);

            var runner = new FallbackAgentRunner(
                providerFactory, options ?? AgentLoopOptions.Default, sessao.Report);

            var resultado = await runner
                .RunAsync(chain, agente.SystemPrompt, sessao.Objective, agente.Tools, sessao.Token)
                .ConfigureAwait(false);

            sessao.Complete(resultado);
        }
        catch (OperationCanceledException)
        {
            sessao.SetStatus(SessionStatus.Cancelled);
        }
        catch (Exception ex)
        {
            // Inclui falhas ao PREPARAR o agente (Node ausente, banco fora do ar):
            // acontecem antes do loop, entao nao passam pelo tratamento dele.
            sessao.Fail($"{ex.Message}");
        }
        finally
        {
            if (vagaTomada) _vagas.Release();
        }
    }

    /// <summary>
    /// Dispara a revisao de uma tarefa ja concluida — OPCIONAL e sob demanda.
    ///
    /// Consome tokens de novo (uma chamada extra, sem ferramentas), por isso nunca
    /// acontece sozinha: quem chama e a interface, depois de o usuario ver o
    /// resultado e decidir que vale a segunda opiniao.
    /// </summary>
    /// <param name="sessao">Tarefa a revisar; precisa ter relatorio.</param>
    /// <param name="reviewers">Cadeia de revisores (ver ReviewRouting).</param>
    /// <param name="providerFactory">Cria o provider de um modelo.</param>
    /// <param name="independent">Se o primeiro revisor e de outro provedor.</param>
    public void StartReview(
        AgentSession sessao,
        IReadOnlyList<ModelInfo> reviewers,
        Func<string, IAiProvider> providerFactory,
        bool independent,
        AgentLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sessao);
        ArgumentNullException.ThrowIfNull(reviewers);
        ArgumentNullException.ThrowIfNull(providerFactory);

        if (reviewers.Count == 0)
        {
            sessao.FailReview(
                "Nao ha outro modelo disponivel para revisar. Cadastre a chave de " +
                "outro provedor para ter uma segunda opiniao independente.");
            return;
        }

        _ = RevisarAsync(sessao, reviewers, providerFactory, independent, options);
    }

    private async Task RevisarAsync(
        AgentSession sessao,
        IReadOnlyList<ModelInfo> reviewers,
        Func<string, IAiProvider> providerFactory,
        bool independent,
        AgentLoopOptions? options)
    {
        var vagaTomada = false;
        try
        {
            sessao.BeginReview(reviewers[0].Id, independent);

            await _vagas.WaitAsync(sessao.Token).ConfigureAwait(false);
            vagaTomada = true;

            await using var revisor = new ReviewAgent(
                sessao.Objective, sessao.FinalText, sessao.ModelId);

            // Revisao e uma leitura so: mais de um punhado de passos indicaria que
            // algo saiu do previsto, e cada passo custa.
            var limites = options ?? _options with { MaxSteps = 2 };

            var runner = new FallbackAgentRunner(providerFactory, limites, sessao.Report);
            var resultado = await runner
                .RunAsync(reviewers, revisor.SystemPrompt, revisor.Objective,
                          revisor.Tools, sessao.Token)
                .ConfigureAwait(false);

            sessao.CompleteReview(resultado);
        }
        catch (OperationCanceledException)
        {
            sessao.FailReview("Revisao interrompida.");
        }
        catch (Exception ex)
        {
            sessao.FailReview($"A revisao falhou: {ex.Message}");
        }
        finally
        {
            if (vagaTomada) _vagas.Release();
        }
    }

    /// <summary>
    /// Interrompe todas as sessoes ativas. Util ao fechar o aplicativo, para nao
    /// deixar navegadores e servidores MCP rodando sem dono.
    /// </summary>
    public void CancelAll()
    {
        foreach (var s in _sessoes.Values.Where(s => s.IsActive))
            s.Cancel();
    }

    /// <summary>Remove uma sessao ja encerrada da lista.</summary>
    public bool Remove(Guid id)
    {
        if (_sessoes.TryGetValue(id, out var s) && s.IsActive) return false;
        if (!_sessoes.TryRemove(id, out var removida)) return false;

        removida.Dispose();
        SessionsChanged?.Invoke();
        return true;
    }
}
