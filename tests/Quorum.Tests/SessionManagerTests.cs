using Quorum.Agents;
using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Tests.Fakes;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Testes da execucao simultanea de sessoes: varias tarefas independentes, cada
/// uma com seu modelo, progresso e cancelamento. Sem IA real e sem rede.
/// </summary>
public class SessionManagerTests
{
    private static ModelInfo Modelo(string id = "m1") =>
        new(AiProvider.Claude, id, id, ModelTier.Balanced, true, 200_000, 1m, 1m);

    /// <summary>Agente falso: expoe uma ferramenta e nao faz nada de verdade.</summary>
    private sealed class FakeAgent : IQuorumAgent
    {
        private readonly Action? _aoDescartar;

        public FakeAgent(Action? aoDescartar = null) => _aoDescartar = aoDescartar;

        public string DisplayName => "Agente falso";
        public string SystemPrompt => "prompt";
        public IToolExecutor Tools { get; } = new SpyToolExecutor();
        public TaskKind TaskKind => TaskKind.Automation;

        public ValueTask DisposeAsync()
        {
            _aoDescartar?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private static Task<IQuorumAgent> FabricaOk(Action<string> log, CancellationToken ct) =>
        Task.FromResult<IQuorumAgent>(new FakeAgent());

    private static async Task<AgentSession> AguardarFim(AgentSession s, int limiteMs = 5000)
    {
        var inicio = DateTime.UtcNow;
        while (s.IsActive || s.Status == SessionStatus.Pending)
        {
            if ((DateTime.UtcNow - inicio).TotalMilliseconds > limiteMs)
                throw new TimeoutException($"Sessao nao encerrou. Estado: {s.Status}");
            await Task.Delay(15);
        }
        return s;
    }

    [Fact]
    public async Task Sessao_conclui_e_guarda_o_relatorio()
    {
        var gerente = new AgentSessionManager();
        var s = gerente.Start("Teste", "valide algo", FabricaOk, new[] { Modelo() },
            _ => new ScriptedProvider(new CompletionResponse("relatorio pronto", Array.Empty<ToolCall>())));

        await AguardarFim(s);

        Assert.Equal(SessionStatus.Completed, s.Status);
        Assert.Equal("relatorio pronto", s.FinalText);
        Assert.Equal("m1", s.ModelId);
    }

    [Fact]
    public async Task Varias_sessoes_rodam_ao_mesmo_tempo()
    {
        var gerente = new AgentSessionManager(maxSimultaneas: 3);

        var sessoes = Enumerable.Range(0, 3).Select(i =>
            gerente.Start($"Tarefa {i}", "objetivo", FabricaOk, new[] { Modelo() },
                _ => new ScriptedProvider(new CompletionResponse($"ok {i}", Array.Empty<ToolCall>()))))
            .ToList();

        foreach (var s in sessoes) await AguardarFim(s);

        Assert.All(sessoes, s => Assert.Equal(SessionStatus.Completed, s.Status));
        Assert.Equal(3, gerente.Sessions.Count);
        Assert.Equal(0, gerente.ActiveCount);
    }

    [Fact]
    public async Task Limite_de_simultaneidade_e_respeitado()
    {
        // Com teto 1, a segunda sessao so comeca depois que a primeira libera a
        // vaga — protege memoria (cada agente de tela sobe um navegador).
        var gerente = new AgentSessionManager(maxSimultaneas: 1);
        var emExecucao = 0;
        var maximoObservado = 0;
        var trava = new object();

        Task<IQuorumAgent> Fabrica(Action<string> log, CancellationToken ct)
        {
            lock (trava)
            {
                emExecucao++;
                maximoObservado = Math.Max(maximoObservado, emExecucao);
            }
            return Task.FromResult<IQuorumAgent>(new FakeAgent(aoDescartar: () =>
            {
                lock (trava) emExecucao--;
            }));
        }

        var sessoes = Enumerable.Range(0, 4).Select(_ =>
            gerente.Start("T", "obj", Fabrica, new[] { Modelo() },
                _ => new ScriptedProvider(new CompletionResponse("ok", Array.Empty<ToolCall>()))))
            .ToList();

        foreach (var s in sessoes) await AguardarFim(s);

        Assert.Equal(1, maximoObservado);
        Assert.All(sessoes, s => Assert.Equal(SessionStatus.Completed, s.Status));
    }

    [Fact]
    public async Task Sessao_pode_ser_cancelada_sem_afetar_as_outras()
    {
        var gerente = new AgentSessionManager(maxSimultaneas: 3);
        var liberar = new TaskCompletionSource();

        // Esta sessao fica presa ate ser cancelada.
        async Task<IQuorumAgent> FabricaLenta(Action<string> log, CancellationToken ct)
        {
            await liberar.Task.WaitAsync(ct);
            return new FakeAgent();
        }

        var lenta = gerente.Start("Lenta", "obj", FabricaLenta, new[] { Modelo() },
            _ => new ScriptedProvider(new CompletionResponse("nunca", Array.Empty<ToolCall>())));

        var rapida = gerente.Start("Rapida", "obj", FabricaOk, new[] { Modelo() },
            _ => new ScriptedProvider(new CompletionResponse("terminei", Array.Empty<ToolCall>())));

        await AguardarFim(rapida);
        Assert.Equal(SessionStatus.Completed, rapida.Status);

        lenta.Cancel();
        await AguardarFim(lenta);

        Assert.Equal(SessionStatus.Cancelled, lenta.Status);
        Assert.Equal(SessionStatus.Completed, rapida.Status); // nao foi afetada
    }

    [Fact]
    public async Task Falha_ao_preparar_o_agente_vira_sessao_falha_com_motivo()
    {
        // Ex.: Node ausente ao subir o servidor MCP. Acontece ANTES do loop, entao
        // precisa ser tratado pelo gerente.
        var gerente = new AgentSessionManager();
        Task<IQuorumAgent> FabricaQuebrada(Action<string> log, CancellationToken ct) =>
            throw new InvalidOperationException("Node.js nao encontrado");

        var s = gerente.Start("Tela", "obj", FabricaQuebrada, new[] { Modelo() },
            _ => new ScriptedProvider());

        await AguardarFim(s);

        Assert.Equal(SessionStatus.Failed, s.Status);
        Assert.Contains("Node.js", s.FinalText);
    }

    [Fact]
    public async Task Cadeia_vazia_falha_com_orientacao()
    {
        var gerente = new AgentSessionManager();
        var s = gerente.Start("X", "obj", FabricaOk, Array.Empty<ModelInfo>(),
            _ => new ScriptedProvider());

        await AguardarFim(s);

        Assert.Equal(SessionStatus.Failed, s.Status);
        Assert.Contains("chave de API", s.FinalText);
    }

    [Fact]
    public async Task Progresso_e_registrado_e_limitado()
    {
        var gerente = new AgentSessionManager();
        var s = gerente.Start("X", "obj", FabricaOk, new[] { Modelo() },
            _ => new ScriptedProvider(new CompletionResponse("ok", Array.Empty<ToolCall>())));

        await AguardarFim(s);

        Assert.Contains(s.Progress, p => p.Contains("Preparando"));
    }

    [Fact]
    public async Task Sessao_ativa_nao_pode_ser_removida()
    {
        var gerente = new AgentSessionManager();
        var liberar = new TaskCompletionSource();
        async Task<IQuorumAgent> Lenta(Action<string> log, CancellationToken ct)
        {
            await liberar.Task.WaitAsync(ct);
            return new FakeAgent();
        }

        var s = gerente.Start("X", "obj", Lenta, new[] { Modelo() }, _ => new ScriptedProvider());
        await Task.Delay(50);

        Assert.False(gerente.Remove(s.Id));   // esta ativa

        s.Cancel();
        await AguardarFim(s);

        Assert.True(gerente.Remove(s.Id));    // encerrada, pode sair da lista
        Assert.Empty(gerente.Sessions);
    }

    [Fact]
    public async Task CancelAll_encerra_todas_as_ativas()
    {
        var gerente = new AgentSessionManager(maxSimultaneas: 3);
        var liberar = new TaskCompletionSource();
        async Task<IQuorumAgent> Lenta(Action<string> log, CancellationToken ct)
        {
            await liberar.Task.WaitAsync(ct);
            return new FakeAgent();
        }

        var sessoes = Enumerable.Range(0, 3)
            .Select(_ => gerente.Start("X", "obj", Lenta, new[] { Modelo() }, _ => new ScriptedProvider()))
            .ToList();
        await Task.Delay(50);

        gerente.CancelAll();
        foreach (var s in sessoes) await AguardarFim(s);

        Assert.All(sessoes, s => Assert.Equal(SessionStatus.Cancelled, s.Status));
    }

    [Fact]
    public void Teto_invalido_e_rejeitado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentSessionManager(0));
    }

    [Fact]
    public async Task Sessao_mostra_o_modelo_que_realmente_respondeu_apos_fallback()
    {
        // "a" falha (operacional) e "b" entrega. O card NAO pode continuar exibindo
        // "a": quem le o custo e o relatorio precisa saber qual IA respondeu.
        var gerente = new AgentSessionManager();
        var s = gerente.Start("Teste", "obj", FabricaOk,
            new[] { Modelo("a"), Modelo("b") },
            id => id == "a"
                ? new ThrowingProvider()
                : new ScriptedProvider(new CompletionResponse("resposta do b", Array.Empty<ToolCall>())));

        await AguardarFim(s);

        Assert.Equal(SessionStatus.Completed, s.Status);
        Assert.Equal("b", s.ModelId);          // e nao "a", o escolhido no inicio
        Assert.Equal("resposta do b", s.FinalText);
    }

    [Fact]
    public async Task Sessao_sem_fallback_mantem_o_modelo_escolhido()
    {
        var gerente = new AgentSessionManager();
        var s = gerente.Start("Teste", "obj", FabricaOk, new[] { Modelo("unico") },
            _ => new ScriptedProvider(new CompletionResponse("ok", Array.Empty<ToolCall>())));

        await AguardarFim(s);

        Assert.Equal("unico", s.ModelId);
    }

    [Fact]
    public async Task Parar_no_teto_de_passos_tem_estado_proprio()
    {
        // A IA pede ferramenta em toda resposta: o loop bate no teto. Isso NAO e
        // "concluida" — o usuario precisa ver a diferenca sem abrir o relatorio.
        var gerente = new AgentSessionManager();
        var respostas = Enumerable.Range(0, 10)
            .Select(i => new CompletionResponse("", new[] { new ToolCall($"c{i}", "navegar", "{}") }))
            .ToArray();

        var s = gerente.Start("Teste", "obj", FabricaOk, new[] { Modelo() },
            _ => new ScriptedProvider(respostas),
            new AgentLoopOptions(MaxSteps: 2));

        await AguardarFim(s);

        Assert.Equal(SessionStatus.StepLimitReached, s.Status);
        Assert.Contains("Limite de passos", s.FinalText);
    }
}
