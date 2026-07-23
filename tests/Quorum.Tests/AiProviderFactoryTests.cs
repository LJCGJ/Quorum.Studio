using Quorum.Providers;
using Quorum.Core.Models;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Verifica o roteamento chave -> provider. Nao ha chamada de rede: apenas a
/// construcao do cliente, entao chaves falsas bastam e nada e cobrado.
/// </summary>
public class AiProviderFactoryTests
{
    [Fact]
    public void Chave_do_claude_cria_provider_do_claude()
    {
        using var provider = AiProviderFactory.Create("sk-ant-fake-key", "claude-haiku-4-5-20251001");
        Assert.Equal(AiProvider.Claude, provider.Provider);
    }

    [Fact]
    public void Chave_da_openai_cria_provider_da_openai()
    {
        using var provider = AiProviderFactory.Create("sk-fake-openai-key", "gpt-4o-mini");
        Assert.Equal(AiProvider.OpenAI, provider.Provider);
    }

    [Fact]
    public void Chave_do_gemini_cria_provider_do_gemini()
    {
        using var provider = AiProviderFactory.Create("AIzaFakeGeminiKey", "gemini-2.5-flash");
        Assert.Equal(AiProvider.Gemini, provider.Provider);
    }

    [Fact]
    public void Chave_no_formato_novo_do_google_tambem_vai_para_o_gemini()
    {
        // O Google ja mudou o formato uma vez (AIza -> AQ.); o Gemini e o padrao.
        using var provider = AiProviderFactory.Create("AQ.Ab8FakeKey", "gemini-2.5-flash");
        Assert.Equal(AiProvider.Gemini, provider.Provider);
    }

    [Theory]
    [InlineData("sk-ant-fake")]
    [InlineData("sk-fake")]
    [InlineData("AIzaFake")]
    public void Modelo_vazio_e_rejeitado_em_qualquer_provedor(string chave)
    {
        Assert.Throws<ArgumentException>(() => AiProviderFactory.Create(chave, "  "));
    }

    [Fact]
    public void Chave_vazia_e_rejeitada()
    {
        Assert.Throws<ArgumentException>(() => AiProviderFactory.Create("  ", "algum-modelo"));
    }
}
