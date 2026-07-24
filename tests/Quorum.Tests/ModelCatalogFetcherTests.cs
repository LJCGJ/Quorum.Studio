using System.Net;
using System.Text;
using Quorum.Core.Models;
using Quorum.Providers;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Testa a leitura da lista de modelos com respostas falsas de cada provedor —
/// sem rede, sem chave real e sem custo.
/// </summary>
public class ModelCatalogFetcherTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _corpo;

        public FakeHandler(string corpo, HttpStatusCode status = HttpStatusCode.OK)
        {
            _corpo = corpo; _status = status;
        }

        public Uri? UltimaUrl { get; private set; }
        public HttpRequestMessage? UltimaRequisicao { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaUrl = request.RequestUri;
            UltimaRequisicao = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_corpo, Encoding.UTF8, "application/json")
            });
        }
    }

    private static ModelCatalogFetcher Comm(string corpo, out FakeHandler h,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        h = new FakeHandler(corpo, status);
        return new ModelCatalogFetcher(new HttpClient(h));
    }

    [Fact]
    public async Task Le_formato_da_anthropic()
    {
        using var f = Comm("""
            {"data":[{"id":"claude-haiku-4-5-20251001"},{"id":"claude-modelo-novo-9"}]}
            """, out var h);

        var modelos = await f.FetchAsync("sk-ant-fake");

        Assert.Equal(2, modelos.Count);
        Assert.All(modelos, m => Assert.Equal(AiProvider.Claude, m.Provider));
        Assert.Contains("api.anthropic.com", h.UltimaUrl!.ToString());
        Assert.True(h.UltimaRequisicao!.Headers.Contains("x-api-key"));
    }

    [Fact]
    public async Task Le_formato_da_openai()
    {
        using var f = Comm("""{"data":[{"id":"gpt-4o-mini"},{"id":"gpt-5-turbo"}]}""", out var h);

        var modelos = await f.FetchAsync("sk-fake");

        Assert.Equal(2, modelos.Count);
        Assert.All(modelos, m => Assert.Equal(AiProvider.OpenAI, m.Provider));
        Assert.Equal("Bearer", h.UltimaRequisicao!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Le_formato_do_gemini_e_tira_o_prefixo_models()
    {
        using var f = Comm("""
            {"models":[
              {"name":"models/gemini-2.5-flash","supportedGenerationMethods":["generateContent"]},
              {"name":"models/text-embedding-004","supportedGenerationMethods":["embedContent"]}
            ]}
            """, out _);

        var modelos = await f.FetchAsync("AIzaFake");

        // O de embedding e descartado: nao serve para conversa nem automacao.
        Assert.Single(modelos);
        Assert.Equal("gemini-2.5-flash", modelos[0].Id);
    }

    [Fact]
    public async Task Modelo_conhecido_mantem_preco_da_tabela()
    {
        using var f = Comm("""{"data":[{"id":"claude-haiku-4-5-20251001"}]}""", out _);

        var modelos = await f.FetchAsync("sk-ant-fake");

        Assert.True(modelos[0].PricingKnown);
        Assert.Equal(1m, modelos[0].CostInputPerMillion);
    }

    [Fact]
    public async Task Modelo_novo_vem_com_preco_marcado_como_desconhecido()
    {
        // Um lancamento que ainda nao esta na nossa tabela: melhor admitir que o
        // preco e desconhecido do que exibir zero, que enganaria o modo economia.
        using var f = Comm("""{"data":[{"id":"claude-modelo-que-nao-existia"}]}""", out _);

        var modelos = await f.FetchAsync("sk-ant-fake");

        Assert.False(modelos[0].PricingKnown);
    }

    [Theory]
    [InlineData("claude-opus-5", ModelTier.Powerful)]
    [InlineData("claude-haiku-9", ModelTier.Fast)]
    [InlineData("gpt-6-mini", ModelTier.Fast)]
    [InlineData("claude-sonnet-9", ModelTier.Balanced)]
    public async Task Faixa_de_modelo_novo_e_inferida_pelo_nome(string id, ModelTier esperado)
    {
        using var f = Comm($$"""{"data":[{"id":"{{id}}"}]}""", out _);

        var modelos = await f.FetchAsync("sk-ant-fake");

        Assert.Equal(esperado, modelos[0].Tier);
    }

    [Fact]
    public async Task Chave_recusada_da_mensagem_clara()
    {
        using var f = Comm("{}", out _, HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ModelFetchException>(
            () => f.FetchAsync("sk-ant-invalida"));

        Assert.Contains("recusada", ex.Message);
    }

    [Fact]
    public async Task Excesso_de_requisicoes_orienta_a_esperar()
    {
        using var f = Comm("{}", out _, HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<ModelFetchException>(() => f.FetchAsync("sk-fake"));

        Assert.Contains("instantes", ex.Message);
    }

    [Fact]
    public async Task Resposta_fora_do_formato_nao_derruba_com_erro_tecnico()
    {
        using var f = Comm("isto nao e json", out _);

        var ex = await Assert.ThrowsAsync<ModelFetchException>(() => f.FetchAsync("sk-fake"));

        Assert.Contains("formato esperado", ex.Message);
    }

    [Fact]
    public async Task Chave_vazia_e_rejeitada_antes_de_chamar_a_rede()
    {
        using var f = Comm("{}", out var h);

        await Assert.ThrowsAsync<ArgumentException>(() => f.FetchAsync("  "));

        Assert.Null(h.UltimaUrl);   // nem chegou a sair requisicao
    }
}
