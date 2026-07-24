using Quorum.Core.Output;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Extracao de scripts e geracao de relatorio: o que transforma a resposta da IA
/// em algo que o usuario leva embora.
/// </summary>
public class CodeBlockExtractorTests
{
    [Fact]
    public void Extrai_bloco_com_linguagem()
    {
        const string texto = """
            Segue o teste:

            ```python
            def test_login():
                assert True
            ```

            Pronto.
            """;

        var blocos = CodeBlockExtractor.Extract(texto);

        Assert.Single(blocos);
        Assert.Equal("python", blocos[0].Language);
        Assert.Contains("def test_login", blocos[0].Code);
        Assert.Equal(".py", blocos[0].FileExtension);
    }

    [Fact]
    public void Extrai_varios_blocos_na_ordem()
    {
        const string texto = """
            Primeiro:
            ```sql
            SELECT 1;
            ```
            Depois:
            ```robot
            *** Test Cases ***
            Caso
                Log    ok
            ```
            """;

        var blocos = CodeBlockExtractor.Extract(texto);

        Assert.Equal(2, blocos.Count);
        Assert.Equal(".sql", blocos[0].FileExtension);
        Assert.Equal(".robot", blocos[1].FileExtension);
    }

    [Fact]
    public void Ultimo_bloco_e_o_escolhido()
    {
        // A IA mostra um trecho parcial e so no fim entrega o script completo:
        // por isso o criterio e a ORDEM, e nao a linguagem.
        const string texto = """
            Exemplo incompleto:
            ```python
            # rascunho
            ```
            Versao final:
            ```python
            # completo
            ```
            """;

        var ultimo = CodeBlockExtractor.Last(texto);

        Assert.NotNull(ultimo);
        Assert.Contains("completo", ultimo!.Code);
        Assert.DoesNotContain("rascunho", ultimo.Code);
    }

    [Fact]
    public void Bloco_sem_linguagem_e_reconhecido_pelo_conteudo()
    {
        const string robot = """
            ```
            *** Settings ***
            Library    SeleniumLibrary
            ```
            """;
        const string sql = """
            ```
            SELECT nome FROM clientes;
            ```
            """;

        Assert.Equal(".robot", CodeBlockExtractor.Last(robot)!.FileExtension);
        Assert.Equal(".sql", CodeBlockExtractor.Last(sql)!.FileExtension);
    }

    [Fact]
    public void Cerca_vazia_nao_vira_arquivo()
    {
        var blocos = CodeBlockExtractor.Extract("```\n\n```");
        Assert.Empty(blocos);
    }

    [Fact]
    public void Texto_sem_codigo_nao_produz_blocos()
    {
        Assert.Empty(CodeBlockExtractor.Extract("Nenhum script desta vez."));
        Assert.Null(CodeBlockExtractor.Last(""));
        Assert.Empty(CodeBlockExtractor.Extract(null));
    }

    [Fact]
    public void Conta_linhas_para_a_interface()
    {
        var b = CodeBlockExtractor.Last("```py\na\nb\nc\n```");
        Assert.Equal(3, b!.LineCount);
    }
}

public class ReportBuilderTests
{
    private static ReportData Dados(string corpo = "Tudo certo.") =>
        new("Teste de API", "Resultado do teste", corpo,
            Model: "claude-haiku-4-5-20251001", Tokens: 1234, Operator: "operador");

    [Fact]
    public void Gera_html_com_titulo_e_ficha()
    {
        var html = ReportBuilder.BuildHtml(Dados());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Teste de API", html);
        Assert.Contains("claude-haiku-4-5-20251001", html);
        // O relatorio formata numeros na cultura da maquina (no Brasil, "1.234").
        // O teste acompanha em vez de fixar um formato, senao passaria so aqui.
        Assert.Contains(1234L.ToString("N0"), html);
        Assert.Contains("Quorum", html);
    }

    [Fact]
    public void Escapa_html_do_conteudo()
    {
        // Um relatorio que contenha <script> nao pode virar script ao abrir.
        var html = ReportBuilder.BuildHtml(Dados("<script>alert('x')</script>"));

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Codigo_sai_destacado_do_texto()
    {
        var corpo = "Analise:\n```python\nassert 1 == 1\n```\nFim.";

        var html = ReportBuilder.BuildHtml(Dados(corpo));

        Assert.Contains("class=\"codigo\"", html);
        Assert.Contains("assert 1 == 1", html);
        Assert.Contains("python", html);
    }

    [Fact]
    public void Campos_ausentes_nao_viram_linhas_vazias()
    {
        var html = ReportBuilder.BuildHtml(
            new ReportData("T", "S", "corpo"));

        Assert.DoesNotContain("<dt>Modelo</dt>", html);
        Assert.DoesNotContain("<dt>Tokens</dt>", html);
        Assert.Contains("<dt>Data</dt>", html);   // data sempre existe
    }

    [Fact]
    public void Dados_nulos_sao_rejeitados()
    {
        Assert.Throws<ArgumentNullException>(() => ReportBuilder.BuildHtml(null!));
    }
}
