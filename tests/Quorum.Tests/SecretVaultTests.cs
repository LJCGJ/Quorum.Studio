using Quorum.Security;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Testes do cofre em pasta temporaria — nao tocam nos dados reais do usuario.
/// Valem nos tres sistemas: o que muda entre eles e a forma de protecao, nao o
/// comportamento observavel.
/// </summary>
public sealed class SecretVaultTests : IDisposable
{
    private readonly string _pasta;
    private readonly FileSecretVault _cofre;

    public SecretVaultTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "quorum-testes-" + Guid.NewGuid().ToString("N"));
        _cofre = new FileSecretVault(_pasta);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch { /* limpeza best-effort */ }
    }

    [Fact]
    public void Guarda_e_recupera_um_segredo()
    {
        _cofre.Save("claude", "sk-ant-exemplo-123");

        Assert.Equal("sk-ant-exemplo-123", _cofre.Load("claude"));
    }

    [Fact]
    public void Segredo_inexistente_devolve_nulo()
    {
        Assert.Null(_cofre.Load("nunca-gravado"));
    }

    [Fact]
    public void Regravar_substitui_o_valor()
    {
        _cofre.Save("openai", "chave-antiga");
        _cofre.Save("openai", "chave-nova");

        Assert.Equal("chave-nova", _cofre.Load("openai"));
        Assert.Single(_cofre.List());
    }

    [Fact]
    public void Lista_os_nomes_guardados()
    {
        _cofre.Save("claude", "a");
        _cofre.Save("gemini", "b");

        var nomes = _cofre.List();

        Assert.Equal(2, nomes.Count);
        Assert.Contains("claude", nomes);
        Assert.Contains("gemini", nomes);
    }

    [Fact]
    public void Remove_um_segredo()
    {
        _cofre.Save("claude", "a");

        Assert.True(_cofre.Delete("claude"));
        Assert.False(_cofre.Delete("claude"));   // ja nao existe
        Assert.Null(_cofre.Load("claude"));
    }

    [Fact]
    public void Limpar_apaga_tudo()
    {
        _cofre.Save("claude", "a");
        _cofre.Save("openai", "b");

        _cofre.Clear();

        Assert.Empty(_cofre.List());
    }

    [Fact]
    public void Sobrevive_a_uma_nova_instancia()
    {
        // E o ponto do cofre: fechar e reabrir o app nao pode perder as chaves.
        _cofre.Save("claude", "sk-ant-persistente");

        var outro = new FileSecretVault(_pasta);

        Assert.Equal("sk-ant-persistente", outro.Load("claude"));
    }

    [Fact]
    public void Arquivo_corrompido_nao_derruba_a_abertura()
    {
        _cofre.Save("claude", "valida");
        File.WriteAllText(Path.Combine(_pasta, "claude.secret"), "isto nao e base64 valido!!");

        // Melhor tratar como ausente do que lancar durante a inicializacao.
        Assert.Null(_cofre.Load("claude"));
    }

    [Fact]
    public void Nome_com_caracteres_de_caminho_nao_escapa_da_pasta()
    {
        // Uma tentativa de "../../etc/senha" nao pode gravar fora do cofre.
        _cofre.Save("../../fuga", "x");

        var arquivos = Directory.GetFiles(_pasta);
        Assert.Single(arquivos);
        Assert.StartsWith(_pasta, Path.GetFullPath(arquivos[0]));
    }

    [Fact]
    public void Nome_sem_caractere_valido_e_rejeitado()
    {
        Assert.Throws<ArgumentException>(() => _cofre.Save("///", "x"));
    }

    [Fact]
    public void Declara_o_nivel_real_de_protecao()
    {
        // A interface usa isto para contar a verdade ao usuario.
        var esperado = OperatingSystem.IsWindows()
            ? VaultProtection.OperatingSystem
            : VaultProtection.FilePermissions;

        Assert.Equal(esperado, _cofre.Protection);
        Assert.Equal(_pasta, _cofre.Location);
    }
}
