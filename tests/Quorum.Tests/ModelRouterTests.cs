using Quorum.Core.Models;
using Quorum.Core.Routing;
using Xunit;

namespace Quorum.Tests;

public class ModelRouterTests
{
    private static ModelRouter NovoRoteador() =>
        new(new ModelRegistry(DefaultCatalog.Models));

    [Fact]
    public void Chat_escolhe_tier_rapido()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat), RoutingPreferences.Default);

        Assert.True(r.Success);
        Assert.Equal(ModelTier.Fast, r.Selected!.Tier);
    }

    [Fact]
    public void Automacao_escolhe_modelo_com_ferramentas_no_tier_equilibrado()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Automation), RoutingPreferences.Default);

        Assert.True(r.Success);
        Assert.True(r.Selected!.SupportsTools);
        Assert.Equal(ModelTier.Balanced, r.Selected.Tier);
    }

    [Fact]
    public void Analise_prioriza_tier_potente()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Analysis), RoutingPreferences.Default);

        Assert.True(r.Success);
        Assert.Equal(ModelTier.Powerful, r.Selected!.Tier);
    }

    [Fact]
    public void Modo_economia_prefere_o_mais_barato_que_serve()
    {
        // Em Analysis o alvo e Powerful, mas com economia o roteador deve evitar
        // subir de tier e ficar no mais barato que ainda atende (sem exigir tools).
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Analysis),
            new RoutingPreferences(EconomyMode: true));

        Assert.True(r.Success);
        Assert.NotEqual(ModelTier.Powerful, r.Selected!.Tier);
    }

    [Fact]
    public void Pin_valido_e_respeitado()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat),
            new RoutingPreferences(PinnedModelId: "claude-opus-4-8"));

        Assert.True(r.Success);
        Assert.Equal("claude-opus-4-8", r.Selected!.Id);
    }

    [Fact]
    public void Pin_inexistente_falha_com_mensagem_clara()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat),
            new RoutingPreferences(PinnedModelId: "modelo-que-nao-existe"));

        Assert.False(r.Success);
        Assert.Contains("aposentado", r.Reason);
    }

    [Fact]
    public void Provedor_preferido_desempata()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Automation),
            new RoutingPreferences(PreferredProvider: AiProvider.Claude));

        Assert.True(r.Success);
        Assert.Equal(AiProvider.Claude, r.Selected!.Provider);
    }

    [Fact]
    public void Contexto_grande_exclui_modelos_de_janela_pequena()
    {
        // Pede 500k tokens de contexto: so os Gemini (1M) atendem; Claude/OpenAI ficam de fora.
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Analysis, EstimatedContextTokens: 500_000),
            RoutingPreferences.Default);

        Assert.True(r.Success);
        Assert.Equal(AiProvider.Gemini, r.Selected!.Provider);
    }

    [Fact]
    public void Sem_modelos_com_ferramentas_a_automacao_falha()
    {
        // Registro so com um modelo sem tool-use
        var semTools = new ModelInfo(AiProvider.Gemini, "fake-sem-tools", "Fake",
            ModelTier.Balanced, SupportsTools: false, ContextWindow: 100_000, 1m, 1m);
        var roteador = new ModelRouter(new ModelRegistry(new[] { semTools }));

        var r = roteador.Route(new TaskDescriptor(TaskKind.Automation), RoutingPreferences.Default);

        Assert.False(r.Success);
        Assert.Contains("ferramentas", r.Reason);
    }

    [Fact]
    public void Fallback_lista_todos_os_candidatos_validos_em_ordem()
    {
        var cadeia = NovoRoteador().RouteWithFallback(
            new TaskDescriptor(TaskKind.Automation), RoutingPreferences.Default);

        // Todos suportam tools; o primeiro e o mais adequado (tier equilibrado).
        Assert.All(cadeia, m => Assert.True(m.SupportsTools));
        Assert.Equal(ModelTier.Balanced, cadeia[0].Tier);
        Assert.True(cadeia.Count > 1); // ha para onde cair se o primeiro falhar
    }

    [Fact]
    public void Fallback_com_pin_traz_so_o_modelo_fixado()
    {
        var cadeia = NovoRoteador().RouteWithFallback(
            new TaskDescriptor(TaskKind.Chat),
            new RoutingPreferences(PinnedModelId: "gpt-4o"));

        Assert.Single(cadeia);
        Assert.Equal("gpt-4o", cadeia[0].Id);
    }

    [Fact]
    public void Roteamento_e_deterministico()
    {
        var roteador = NovoRoteador();
        var t = new TaskDescriptor(TaskKind.Automation);
        var a = roteador.Route(t, RoutingPreferences.Default);
        var b = roteador.Route(t, RoutingPreferences.Default);
        Assert.Equal(a.Selected!.Id, b.Selected!.Id); // mesma entrada, mesma saida
    }

    // ================= disponibilidade de provedor =================
    // Sem chave de um provedor, seus modelos nao sao opcao: a fabrica detecta o
    // provedor PELA CHAVE, entao rotear para um modelo sem chave criaria um
    // cliente de um provedor pedindo o modelo de outro — falha paga.

    private static IReadOnlySet<AiProvider> Apenas(params AiProvider[] p) => p.ToHashSet();

    [Fact]
    public void So_escolhe_modelo_de_provedor_com_chave()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat),
            new RoutingPreferences(AvailableProviders: Apenas(AiProvider.Claude)));

        Assert.True(r.Success);
        Assert.Equal(AiProvider.Claude, r.Selected!.Provider);
    }

    [Fact]
    public void Cadeia_de_fallback_so_traz_provedores_com_chave()
    {
        var cadeia = NovoRoteador().RouteWithFallback(
            new TaskDescriptor(TaskKind.Automation),
            new RoutingPreferences(AvailableProviders: Apenas(AiProvider.Gemini, AiProvider.OpenAI)));

        Assert.NotEmpty(cadeia);
        Assert.All(cadeia, m => Assert.NotEqual(AiProvider.Claude, m.Provider));
    }

    [Fact]
    public void Sem_nenhuma_chave_a_mensagem_orienta_a_cadastrar()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat),
            new RoutingPreferences(AvailableProviders: new HashSet<AiProvider>()));

        Assert.False(r.Success);
        Assert.Contains("Nenhuma chave de API", r.Reason);
    }

    [Fact]
    public void Pin_de_modelo_sem_chave_do_provedor_falha_com_motivo_claro()
    {
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat),
            new RoutingPreferences(
                PinnedModelId: "gpt-4o",
                AvailableProviders: Apenas(AiProvider.Claude)));

        Assert.False(r.Success);
        Assert.Contains("nao ha chave de API cadastrada", r.Reason);
    }

    [Fact]
    public void Sem_restricao_de_provedor_todos_continuam_valendo()
    {
        // Compatibilidade: preferencias sem a lista (ou vazia) nao filtram nada.
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Chat), RoutingPreferences.Default);

        Assert.True(r.Success);
    }
}
