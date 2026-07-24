using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Quorum.Security;

/// <summary>
/// Cofre em arquivo, um arquivo por segredo.
///
/// No Windows o conteudo e cifrado com DPAPI no escopo do usuario: mesmo com
/// acesso ao disco, outra conta nao le. Nos demais sistemas nao ha equivalente
/// direto sem dependencias nativas, entao o conteudo fica em texto e a protecao
/// vem das permissoes do arquivo (0600) — e <see cref="Protection"/> declara isso,
/// para a interface nao prometer o que nao entrega.
/// </summary>
public sealed class FileSecretVault : ISecretVault
{
    private readonly string _pasta;

    /// <param name="pasta">
    /// Onde guardar. Nulo usa a pasta de dados do usuario
    /// (%APPDATA%\Quorum no Windows, ~/.config/quorum nos demais).
    /// </param>
    public FileSecretVault(string? pasta = null)
    {
        _pasta = pasta ?? PastaPadrao();
        Directory.CreateDirectory(_pasta);
        RestringirPermissoes(_pasta);
    }

    public VaultProtection Protection => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? VaultProtection.OperatingSystem
        : VaultProtection.FilePermissions;

    public string Location => _pasta;

    public void Save(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);

        var bytes = Encoding.UTF8.GetBytes(value);
        var conteudo = Protegido()
            ? Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser))
            : Convert.ToBase64String(bytes);

        var arquivo = Caminho(name);
        File.WriteAllText(arquivo, conteudo);
        RestringirPermissoes(arquivo);
    }

    public string? Load(string name)
    {
        var arquivo = Caminho(name);
        if (!File.Exists(arquivo)) return null;

        try
        {
            var bruto = Convert.FromBase64String(File.ReadAllText(arquivo));
            var bytes = Protegido()
                ? ProtectedData.Unprotect(bruto, null, DataProtectionScope.CurrentUser)
                : bruto;
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            // Arquivo corrompido, de outra conta de usuario, ou de outra maquina:
            // tratar como ausente e melhor que derrubar o aplicativo na abertura.
            return null;
        }
    }

    public IReadOnlyList<string> List()
    {
        if (!Directory.Exists(_pasta)) return Array.Empty<string>();

        return Directory.GetFiles(_pasta, "*.secret")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public bool Delete(string name)
    {
        var arquivo = Caminho(name);
        if (!File.Exists(arquivo)) return false;

        File.Delete(arquivo);
        return true;
    }

    public void Clear()
    {
        foreach (var nome in List()) Delete(nome);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Se ha cifragem do sistema operacional disponivel (DPAPI, so no Windows).
    ///
    /// A anotacao ensina o analisador de portabilidade que este metodo E uma
    /// verificacao de plataforma: sem ela, ele acusa as chamadas a ProtectedData
    /// como inseguras em Linux e macOS — e estaria certo, porque um helper comum
    /// nao prova nada para a analise estatica.
    /// </summary>
    [SupportedOSPlatformGuard("windows")]
    private static bool Protegido() => OperatingSystem.IsWindows();

    private string Caminho(string name)
    {
        // O nome vira arquivo: so caracteres seguros, para nao escapar da pasta.
        var seguro = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (seguro.Length == 0) throw new ArgumentException("Nome de segredo invalido.", nameof(name));
        return Path.Combine(_pasta, seguro + ".secret");
    }

    private static string PastaPadrao()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quorum");

        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config))
            config = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(config, "quorum");
    }

    /// <summary>
    /// Fecha o acesso a outros usuarios do sistema. Em Unix isso e o que protege o
    /// arquivo, ja que o conteudo nao esta cifrado ali.
    /// </summary>
    private static void RestringirPermissoes(string caminho)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            var modo = Directory.Exists(caminho)
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite;

            if (Directory.Exists(caminho)) File.SetUnixFileMode(caminho, modo);
            else if (File.Exists(caminho)) File.SetUnixFileMode(caminho, modo);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Sistema de arquivos sem suporte a permissoes Unix: nao ha o que fazer
            // aqui, e Protection ja avisa que a garantia e limitada.
        }
    }
}
