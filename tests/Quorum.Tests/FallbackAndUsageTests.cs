using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Core.Routing;
using Quorum.Agents;
using Quorum.Tests.Fakes;
using Xunit;

namespace Quorum.Tests;

public class FallbackAndUsageTests
{
    private static ToolCall UmaChamada(string id = "c1") =>
        new(id, "navegar", "{\"url\":\"https://exemplo.com\"}");

    // --- Uso de tokens e truncamento (melhorias no AgentLoop/CompletionResponse) ---

    [Fact]
    public async Task Total_de_tokens_e_somado_pelos_passos()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }, new TokenUsage(100, 20)),
            new CompletionResponse("fim", Array.Empty<ToolCall>(), new TokenUsage(50, 10)));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor());

        Assert.Equal(180, r.TotalTokens); // (100+20) + (50+10)
    }

    [Fact]
    public async Task Total_de_tokens_e_nulo_quando_provedor_nao_informa()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("fim", Array.Empty<ToolCall>())); // sem Usage
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor());

        Assert.Null(r.TotalTokens);
    }

    [Fact]
    public async Task Resposta_truncada_marca_OutputTruncated()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("relatorio cortado...", Array.Empty<ToolCall>(),
                Usage: null, Truncated: true));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor());

        Assert.True(r.OutputTruncated);
        Assert.Equal(AgentStopReason.Completed, r.StopReason);
    }

    // --- Cadeia de fallback (FallbackAgentRunner) ---

    private static ModelInfo Modelo(string id, AiProvider p = AiProvider.Claude) =>
        new(p, id, id, ModelTier.Balanced, SupportsTools: true, 200_000, 1m, 1m);

    [Fact]
    public async Task Primeiro_modelo_sucesso_nao_tenta_os_outros()
    {
        var usados = new List<string>();
        var runner = new FallbackAgentRunner(id =>
        {
            usados.Add(id);
            return new ScriptedProvider(
                new CompletionResponse("ok", Array.Empty<ToolCall>()));
        });

        var chain = new[] { Modelo("a"), Modelo("b"), Modelo("c") };
        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Single(usados);          // so o primeiro foi instanciado
        Assert.Equal("a", usados[0]);
    }

    [Fact]
    public async Task Falha_do_primeiro_cai_para_o_segundo()
    {
        var usados = new List<string>();
        var runner = new FallbackAgentRunner(id =>
        {
            usados.Add(id);
            // "a" lanca (falha de IA); "b" conclui.
            return id == "a"
                ? new ThrowingProvider()
                : new ScriptedProvider(new CompletionResponse("ok pelo B", Array.Empty<ToolCall>()));
        });

        var chain = new[] { Modelo("a"), Modelo("b", AiProvider.Gemini) };
        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal("ok pelo B", r.FinalText);
        Assert.Equal(new[] { "a", "b" }, usados); // tentou os dois, em ordem
    }

    [Fact]
    public async Task Todos_falham_devolve_failed_com_aviso()
    {
        var runner = new FallbackAgentRunner(_ => new ThrowingProvider());
        var chain = new[] { Modelo("a"), Modelo("b") };

        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Failed, r.StopReason);
        Assert.Contains("Todos os modelos", r.FinalText);
    }

    [Fact]
    public async Task Cadeia_vazia_devolve_failed_explicativo()
    {
        var runner = new FallbackAgentRunner(_ => new ScriptedProvider());
        var r = await runner.RunAsync(
            Array.Empty<ModelInfo>(), "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Failed, r.StopReason);
        Assert.Equal(0, r.StepsUsed);
    }

    [Fact]
    public async Task Limite_de_passos_nao_dispara_fallback()
    {
        // StepLimitReached e resultado legitimo: nao se tenta outro modelo.
        var usados = new List<string>();
        var runner = new FallbackAgentRunner(id =>
        {
            usados.Add(id);
            // sempre pede ferramenta: nunca conclui, bate no limite de passos
            return new ScriptedProvider(
                Enumerable.Range(0, 5)
                    .Select(_ => new CompletionResponse("", new[] { UmaChamada() }))
                    .ToArray());
        }, new AgentLoopOptions(MaxSteps: 2));

        var chain = new[] { Modelo("a"), Modelo("b") };
        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.StepLimitReached, r.StopReason);
        Assert.Single(usados); // nao caiu para o "b"
    }

    [Fact]
    public async Task Integra_com_a_cadeia_real_do_ModelRouter()
    {
        // Usa o roteador de verdade para montar a cadeia, provando que as pecas
        // da Fase A e da Fase B se encaixam.
        var router = new ModelRouter(new ModelRegistry(DefaultCatalog.Models));
        var chain = router.RouteWithFallback(
            new TaskDescriptor(TaskKind.Automation), RoutingPreferences.Default);

        var runner = new FallbackAgentRunner(_ =>
            new ScriptedProvider(new CompletionResponse("ok", Array.Empty<ToolCall>())));
        var r = await runner.RunAsync(chain, "sys", "automatize", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.True(chain.Count > 1); // ha alternativas reais para cair
    }

    [Fact]
    public async Task Tokens_da_tentativa_que_concluiu_chegam_ao_resultado()
    {
        // "a" falha (operacional, 0 tokens contabilizados pelo loop); "b" conclui
        // reportando 40 tokens. O total do resultado deve refletir o que "b" gastou.
        var runner = new FallbackAgentRunner(id =>
            id == "a" ? new ThrowingProvider() : new UsageProvider(40));
        var chain = new[] { Modelo("a"), Modelo("b", AiProvider.Gemini) };

        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal(40, r.TotalTokens);
    }

    [Fact]
    public async Task Sucesso_direto_reporta_os_tokens_do_modelo()
    {
        var runner = new FallbackAgentRunner(_ => new UsageProvider(120));
        var chain = new[] { Modelo("a") };

        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal(120, r.TotalTokens);
    }

    [Fact]
    public async Task Cancelamento_no_fallback_devolve_cancelled_sem_lancar()
    {
        // Token ja cancelado: o runner nao deve lancar, e sim devolver Cancelled
        // (contrato uniforme com o AgentLoop — RunAsync nunca lanca por cancelamento).
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new FallbackAgentRunner(
            _ => new ScriptedProvider(new CompletionResponse("ok", Array.Empty<ToolCall>())));
        var chain = new[] { Modelo("a"), Modelo("b") };

        var r = await runner.RunAsync(chain, "sys", "teste", new SpyToolExecutor(), cts.Token);

        Assert.Equal(AgentStopReason.Cancelled, r.StopReason);
    }
}
