using Quorum.Agents;
using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Core.Routing;
using Quorum.Tests.Fakes;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// O "quorum": uma segunda IA revisando criticamente o resultado da primeira.
/// Opcional e sob demanda — nunca dispara sozinha, porque custa tokens.
/// </summary>
public class ReviewRoutingTests
{
    private static ModelRegistry Catalogo() => new(DefaultCatalog.Models);

    private static RoutingPreferences Com(params AiProvider[] p) =>
        new(AvailableProviders: p.ToHashSet());

    [Fact]
    public void Prefere_revisor_de_outro_provedor()
    {
        // A segunda opiniao so vale se vier de outra cabeca.
        var revisores = ReviewRouting.SelectReviewers(
            Catalogo(), Com(AiProvider.Claude, AiProvider.Gemini),
            "claude-haiku-4-5-20251001");

        Assert.NotEmpty(revisores);
        Assert.Equal(AiProvider.Gemini, revisores[0].Provider);
    }

    [Fact]
    public void Nunca_escolhe_o_proprio_modelo_que_escreveu()
    {
        var revisores = ReviewRouting.SelectReviewers(
            Catalogo(), Com(AiProvider.Claude), "claude-haiku-4-5-20251001");

        Assert.DoesNotContain(revisores, m => m.Id == "claude-haiku-4-5-20251001");
    }

    [Fact]
    public void Com_um_provedor_so_usa_outro_modelo_do_mesmo()
    {
        var revisores = ReviewRouting.SelectReviewers(
            Catalogo(), Com(AiProvider.Claude), "claude-haiku-4-5-20251001");

        Assert.NotEmpty(revisores);
        Assert.All(revisores, m => Assert.Equal(AiProvider.Claude, m.Provider));
    }

    [Fact]
    public void Prefere_modelo_mais_capaz_para_revisar()
    {
        // Revisao pede raciocinio, nao velocidade.
        var revisores = ReviewRouting.SelectReviewers(
            Catalogo(), Com(AiProvider.Claude), "claude-haiku-4-5-20251001");

        Assert.Equal(ModelTier.Powerful, revisores[0].Tier);
    }

    [Fact]
    public void Sem_alternativa_a_lista_vem_vazia()
    {
        var so_um = new ModelRegistry(new[]
        {
            new ModelInfo(AiProvider.Claude, "unico", "Unico", ModelTier.Fast, true, 200_000, 1m, 1m)
        });

        var revisores = ReviewRouting.SelectReviewers(so_um, Com(AiProvider.Claude), "unico");

        Assert.Empty(revisores);
    }

    [Fact]
    public void Reconhece_quando_a_opiniao_e_independente()
    {
        var reg = Catalogo();
        var gemini = reg.All.First(m => m.Provider == AiProvider.Gemini);
        var claude = reg.All.First(m => m.Provider == AiProvider.Claude);

        Assert.True(ReviewRouting.IsIndependent(reg, "claude-haiku-4-5-20251001", gemini));
        Assert.False(ReviewRouting.IsIndependent(reg, "claude-haiku-4-5-20251001", claude));
    }
}

public class ReviewSessionTests
{
    private static ModelInfo Modelo(string id = "revisor") =>
        new(AiProvider.Gemini, id, id, ModelTier.Powerful, true, 1_000_000, 1m, 1m);

    private sealed class FakeAgent : IQuorumAgent
    {
        public string DisplayName => "Falso";
        public string SystemPrompt => "p";
        public IToolExecutor Tools { get; } = new SpyToolExecutor();
        public TaskKind TaskKind => TaskKind.Automation;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static Task<IQuorumAgent> Fabrica(Action<string> log, CancellationToken ct) =>
        Task.FromResult<IQuorumAgent>(new FakeAgent());

    private static async Task Aguardar(Func<bool> pronto, int limiteMs = 5000)
    {
        var inicio = DateTime.UtcNow;
        while (!pronto())
        {
            if ((DateTime.UtcNow - inicio).TotalMilliseconds > limiteMs)
                throw new TimeoutException("nao concluiu a tempo");
            await Task.Delay(15);
        }
    }

    [Fact]
    public async Task Revisao_produz_texto_e_contabiliza_tokens_a_parte()
    {
        var gerente = new AgentSessionManager();
        var s = gerente.Start("Teste", "valide o login", Fabrica, new[] { Modelo("autor") },
            _ => new ScriptedProvider(new CompletionResponse("Passou.", Array.Empty<ToolCall>())));
        await Aguardar(() => !s.IsActive);

        gerente.StartReview(s, new[] { Modelo("revisor") },
            _ => new UsageProvider(500), independent: true);
        await Aguardar(() => s.ReviewStatus is not null && s.ReviewStatus != SessionStatus.Running);

        Assert.Equal(SessionStatus.Completed, s.ReviewStatus);
        Assert.Equal("concluido", s.ReviewText);
        Assert.Equal(500, s.ReviewTokens);
        Assert.True(s.ReviewIsIndependent);
        // Os tokens do teste original seguem separados dos da revisao.
        Assert.NotEqual(s.TotalTokens, s.ReviewTokens);
    }

    [Fact]
    public async Task Sem_revisor_disponivel_explica_o_motivo()
    {
        var gerente = new AgentSessionManager();
        var s = gerente.Start("Teste", "obj", Fabrica, new[] { Modelo() },
            _ => new ScriptedProvider(new CompletionResponse("ok", Array.Empty<ToolCall>())));
        await Aguardar(() => !s.IsActive);

        gerente.StartReview(s, Array.Empty<ModelInfo>(), _ => new ScriptedProvider(), false);

        Assert.Equal(SessionStatus.Failed, s.ReviewStatus);
        Assert.Contains("outro provedor", s.ReviewText);
    }

    [Fact]
    public async Task Falha_do_revisor_nao_apaga_o_resultado_original()
    {
        // O relatorio do teste continua valendo mesmo se a revisao falhar.
        var gerente = new AgentSessionManager();
        var s = gerente.Start("Teste", "obj", Fabrica, new[] { Modelo("autor") },
            _ => new ScriptedProvider(new CompletionResponse("relatorio original", Array.Empty<ToolCall>())));
        await Aguardar(() => !s.IsActive);

        gerente.StartReview(s, new[] { Modelo("revisor") },
            _ => new ThrowingProvider(), independent: true);
        await Aguardar(() => s.ReviewStatus is not null && s.ReviewStatus != SessionStatus.Running);

        Assert.Equal(SessionStatus.Failed, s.ReviewStatus);
        Assert.Equal("relatorio original", s.FinalText);
        Assert.Equal(SessionStatus.Completed, s.Status);
    }

    [Fact]
    public void Agente_revisor_nao_expoe_ferramentas()
    {
        // Revisao e leitura critica: nao renavega nem reexecuta o teste.
        var revisor = new ReviewAgent("objetivo", "relatorio", "claude-haiku-4-5-20251001");

        Assert.Empty(revisor.Tools.Tools);
        Assert.Equal(TaskKind.Analysis, revisor.TaskKind);
        Assert.Contains("relatorio", revisor.Objective);
        Assert.Contains("claude-haiku", revisor.Objective);
        Assert.Contains("REVISAR", revisor.SystemPrompt);
    }
}
