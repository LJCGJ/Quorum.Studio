using Quorum.Core.Models;
using Quorum.Core.Routing;
using Xunit;

namespace Quorum.Tests;

public class ModelRegistryTests
{
    [Theory]
    [InlineData("sk-ant-api03-abc", AiProvider.Claude)]
    [InlineData("sk-proj-xyz", AiProvider.OpenAI)]
    [InlineData("sk-abc123", AiProvider.OpenAI)]
    [InlineData("AIzaSyAbc", AiProvider.Gemini)]
    [InlineData("AQ.Ab8xyz", AiProvider.Gemini)]      // formato novo de chave Google (2026)
    [InlineData("qualquer-outra-coisa", AiProvider.Gemini)] // Gemini e o padrao
    public void DetectProvider_por_prefixo(string chave, AiProvider esperado)
    {
        Assert.Equal(esperado, ModelRegistry.DetectProvider(chave));
    }

    [Fact]
    public void DetectProvider_com_chave_vazia_lanca()
    {
        Assert.Throws<ArgumentException>(() => ModelRegistry.DetectProvider("  "));
    }

    [Fact]
    public void AddOrIgnore_nao_duplica_por_id()
    {
        var reg = new ModelRegistry();
        var m = DefaultCatalog.Models[0];
        reg.AddOrIgnore(m);
        reg.AddOrIgnore(m);
        Assert.Single(reg.All);
    }

    [Fact]
    public void Replace_troca_todo_o_catalogo()
    {
        var reg = new ModelRegistry(DefaultCatalog.Models);
        reg.Replace(new[] { DefaultCatalog.Models[0] });
        Assert.Single(reg.All);
    }

    [Fact]
    public void FindById_retorna_nulo_para_modelo_aposentado()
    {
        var reg = new ModelRegistry(DefaultCatalog.Models);
        Assert.Null(reg.FindById("claude-3-5-sonnet")); // modelo que a v4.2 sofreu quando aposentaram
    }
}
