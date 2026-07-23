using Quorum.Core.Models;
using Quorum.Core.Routing;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Testes nascidos da revisao da Fase A: cobrem os casos de borda que a
/// primeira leva nao cobria (pin x contexto, fallback com pin invalido,
/// concorrencia no registro).
/// </summary>
public class RevisaoFaseATests
{
    private static ModelRouter NovoRoteador() =>
        new(new ModelRegistry(DefaultCatalog.Models));

    [Fact]
    public void Pin_com_janela_insuficiente_falha_com_mensagem_clara()
    {
        // gpt-4o tem janela de 128k; a tarefa estima 500k.
        var r = NovoRoteador().Route(
            new TaskDescriptor(TaskKind.Analysis, EstimatedContextTokens: 500_000),
            new RoutingPreferences(PinnedModelId: "gpt-4o"));

        Assert.False(r.Success);
        Assert.Contains("janela", r.Reason);
    }

    [Fact]
    public void Fallback_com_pin_invalido_devolve_cadeia_vazia_e_Route_explica()
    {
        var roteador = NovoRoteador();
        var task = new TaskDescriptor(TaskKind.Chat);
        var prefs = new RoutingPreferences(PinnedModelId: "modelo-fantasma");

        var cadeia = roteador.RouteWithFallback(task, prefs);
        var diagnostico = roteador.Route(task, prefs);

        Assert.Empty(cadeia);                       // contrato: vazio = nada serve
        Assert.False(diagnostico.Success);          // e Route da a razao legivel
        Assert.False(string.IsNullOrWhiteSpace(diagnostico.Reason));
    }

    [Fact]
    public void Economia_continua_respeitando_requisito_de_ferramentas()
    {
        // So um modelo tem tools e ele e caro (Powerful): mesmo em economia,
        // o requisito obrigatorio vence — o barato sem tools NAO pode ser escolhido.
        var comTools = new ModelInfo(AiProvider.Claude, "caro-com-tools", "Caro",
            ModelTier.Powerful, SupportsTools: true, ContextWindow: 200_000, 5m, 25m);
        var baratoSemTools = new ModelInfo(AiProvider.Gemini, "barato-sem-tools", "Barato",
            ModelTier.Fast, SupportsTools: false, ContextWindow: 200_000, 0.1m, 0.4m);
        var roteador = new ModelRouter(new ModelRegistry(new[] { comTools, baratoSemTools }));

        var r = roteador.Route(
            new TaskDescriptor(TaskKind.Automation),
            new RoutingPreferences(EconomyMode: true));

        Assert.True(r.Success);
        Assert.Equal("caro-com-tools", r.Selected!.Id);
    }

    [Fact]
    public async Task Registro_aguenta_leitura_e_substituicao_concorrentes()
    {
        // Simula a UI enumerando enquanto a busca de modelos substitui o catalogo.
        // Sem o lock/snapshot, isto explodia com "collection was modified".
        var reg = new ModelRegistry(DefaultCatalog.Models);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var escritor = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
                reg.Replace(DefaultCatalog.Models);
        });
        var leitor = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var soma = 0;
                foreach (var m in reg.All) soma += m.ContextWindow;
                Assert.True(soma > 0);
            }
        });

        await Task.WhenAll(escritor, leitor);   // nenhum dos dois pode lancar
    }

    [Fact]
    public void All_devolve_snapshot_e_nao_a_lista_interna()
    {
        var reg = new ModelRegistry(DefaultCatalog.Models);
        var antes = reg.All;
        reg.Replace(new[] { DefaultCatalog.Models[0] });

        Assert.Equal(DefaultCatalog.Models.Count, antes.Count); // snapshot preservado
        Assert.Single(reg.All);                                  // estado novo visivel
    }
}
