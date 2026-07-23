using Quorum.Core.Ai;
using Quorum.Core.Models;

namespace Quorum.Agents;

/// <summary>
/// Executa uma tarefa percorrendo a CADEIA DE FALLBACK do roteador: tenta o
/// modelo ideal e, se a chamada a IA falhar de forma nao recuperavel (chave
/// invalida, cota estourada, sem rede), passa AUTOMATICAMENTE para o proximo
/// modelo da cadeia — que pode ser de outro provedor.
///
/// Esta e a peca que realiza a promessa central do Quorum: varias IAs cobrindo
/// umas as outras. O <c>ModelRouter</c> decide a ORDEM (ja testado na Fase A); o
/// <c>AgentLoop</c> sabe encerrar com <see cref="AgentStopReason.Failed"/>; esta
/// classe liga os dois.
///
/// So troca de modelo em <see cref="AgentStopReason.Failed"/>. Conclusao, limite
/// de passos e cancelamento sao resultados legitimos — nao se tenta outro modelo
/// (seria desperdicio de tokens repetir uma tarefa que ja rodou).
/// </summary>
public sealed class FallbackAgentRunner
{
    private readonly Func<string, IAiProvider> _providerFactory;
    private readonly AgentLoopOptions _options;
    private readonly Action<string>? _onProgress;

    /// <param name="providerFactory">
    /// Cria um <see cref="IAiProvider"/> para um modelo. Recebe o Id do modelo e
    /// devolve o provider pronto (a fabrica ja resolve a chave/credencial certa).
    /// </param>
    /// <param name="options">Limites do loop.</param>
    /// <param name="onProgress">Callback de progresso (opcional).</param>
    public FallbackAgentRunner(
        Func<string, IAiProvider> providerFactory,
        AgentLoopOptions? options = null,
        Action<string>? onProgress = null)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _options = options ?? AgentLoopOptions.Default;
        _onProgress = onProgress;
    }

    /// <summary>
    /// Roda a tarefa tentando cada modelo da cadeia ate uma execucao NAO falhar.
    /// </summary>
    /// <param name="chain">Cadeia de modelos do roteador (ideal primeiro).</param>
    /// <param name="systemPrompt">Instrucao de sistema.</param>
    /// <param name="objective">Objetivo do usuario.</param>
    /// <param name="executor">Executor de ferramentas.</param>
    /// <param name="ct">Cancelamento.</param>
    /// <returns>
    /// O resultado da primeira execucao que nao falhou. Se TODOS falharem, devolve
    /// o ultimo resultado <see cref="AgentStopReason.Failed"/>, com o texto
    /// somando as tentativas. Cadeia vazia produz um Failed explicativo.
    /// </returns>
    public async Task<AgentResult> RunAsync(
        IReadOnlyList<ModelInfo> chain,
        string systemPrompt,
        string objective,
        IToolExecutor executor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(executor);

        if (chain.Count == 0)
            return new AgentResult(
                "Nenhum modelo disponivel para executar a tarefa. Verifique a chave de " +
                "API e a lista de modelos.",
                AgentStopReason.Failed,
                StepsUsed: 0);

        AgentResult? ultimaFalha = null;
        long? tokensAcumulados = null;

        for (var i = 0; i < chain.Count; i++)
        {
            // Cancelamento entre tentativas: devolve Cancelled como resultado (nao
            // lanca), para o contrato ser uniforme — RunAsync nunca lanca por
            // cancelamento, seja qual for o instante em que ele ocorre.
            if (ct.IsCancellationRequested)
                return new AgentResult(
                    ultimaFalha?.FinalText ?? "Operacao cancelada.",
                    AgentStopReason.Cancelled,
                    StepsUsed: 0,
                    tokensAcumulados);

            var model = chain[i];

            using var provider = _providerFactory(model.Id);
            var loop = new AgentLoop(provider, _options, _onProgress);

            if (i > 0)
                Report($">>> Tentando modelo alternativo: {model.DisplayName} ({model.Id})");

            var result = await loop.RunAsync(model.Id, systemPrompt, objective, executor, ct)
                .ConfigureAwait(false);

            // Soma os tokens desta tentativa ao total (tentativas que falharam no
            // meio ja consumiram tokens; o custo real precisa contabiliza-las).
            tokensAcumulados = Somar(tokensAcumulados, result.TotalTokens);

            // Qualquer coisa que NAO seja falha e um resultado legitimo: entrega,
            // com o total de tokens de todas as tentativas ate aqui.
            if (result.StopReason != AgentStopReason.Failed)
                return result with { TotalTokens = tokensAcumulados };

            ultimaFalha = result;
            Report($">>> Modelo {model.DisplayName} falhou; " +
                   (i + 1 < chain.Count ? "tentando o proximo." : "sem mais alternativas."));
        }

        // Todos falharam: devolve a ultima falha, deixando claro que a cadeia acabou.
        return ultimaFalha! with
        {
            FinalText = ultimaFalha.FinalText +
                        "\n\n[Todos os modelos disponiveis falharam. Verifique conexao, " +
                        "chaves de API e limites de uso dos provedores.]",
            TotalTokens = tokensAcumulados
        };
    }

    private static long? Somar(long? a, long? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a + b;
    }

    private void Report(string message) => _onProgress?.Invoke(message);
}
