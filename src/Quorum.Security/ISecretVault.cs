namespace Quorum.Security;

/// <summary>Como os segredos estao protegidos em disco.</summary>
public enum VaultProtection
{
    /// <summary>
    /// Cifrado pelo sistema operacional, amarrado a conta do usuario (DPAPI no
    /// Windows). Outra conta na mesma maquina nao consegue ler.
    /// </summary>
    OperatingSystem,

    /// <summary>
    /// Apenas permissoes de arquivo restritivas — o conteudo esta em texto claro
    /// para quem tiver acesso de administrador ou ao disco.
    /// </summary>
    FilePermissions
}

/// <summary>
/// Guarda segredos (chaves de API) entre execucoes do aplicativo.
///
/// A interface existe para o app nao depender do mecanismo: no Windows ha cifragem
/// do proprio sistema; em outros sistemas, a protecao e mais fraca — e o app diz
/// isso ao usuario em vez de sugerir uma seguranca que nao tem.
/// </summary>
public interface ISecretVault
{
    /// <summary>Nivel real de protecao, para a interface ser honesta a respeito.</summary>
    VaultProtection Protection { get; }

    /// <summary>Onde os segredos ficam, para o usuario poder inspecionar ou apagar.</summary>
    string Location { get; }

    /// <summary>Grava (ou substitui) um segredo.</summary>
    void Save(string name, string value);

    /// <summary>Le um segredo; nulo se nao existir ou nao puder ser decifrado.</summary>
    string? Load(string name);

    /// <summary>Nomes de todos os segredos guardados.</summary>
    IReadOnlyList<string> List();

    /// <summary>Remove um segredo. Devolve false se ele nao existia.</summary>
    bool Delete(string name);

    /// <summary>Apaga tudo — usado quando o usuario desliga a lembranca de chaves.</summary>
    void Clear();
}
