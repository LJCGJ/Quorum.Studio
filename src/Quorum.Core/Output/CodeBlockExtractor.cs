using System.Text.RegularExpressions;

namespace Quorum.Core.Output;

/// <summary>Um bloco de codigo encontrado numa resposta da IA.</summary>
/// <param name="Language">Linguagem declarada na cerca (vazia se nao houver).</param>
/// <param name="Code">Conteudo do bloco, sem as cercas.</param>
public sealed record CodeBlock(string Language, string Code)
{
    /// <summary>
    /// Extensao de arquivo adequada. Quando a linguagem nao foi declarada, tenta
    /// reconhecer pelo proprio conteudo — a IA nem sempre rotula a cerca, e salvar
    /// tudo como .txt tornaria o script inutil no editor.
    /// </summary>
    public string FileExtension => Language.ToLowerInvariant() switch
    {
        "python" or "py" => ".py",
        "robot" or "robotframework" => ".robot",
        "sql" => ".sql",
        "csharp" or "c#" or "cs" => ".cs",
        "javascript" or "js" => ".js",
        "typescript" or "ts" => ".ts",
        "bash" or "sh" or "shell" => ".sh",
        "powershell" or "ps1" => ".ps1",
        "json" => ".json",
        "yaml" or "yml" => ".yaml",
        "" => InferirPeloConteudo(),
        _ => ".txt"
    };

    /// <summary>Nome amigavel da linguagem, para exibir na interface.</summary>
    public string DisplayLanguage =>
        Language.Length > 0 ? Language : FileExtension.TrimStart('.');

    /// <summary>Quantas linhas o bloco tem (util para a interface).</summary>
    public int LineCount => Code.Split('\n').Length;

    private string InferirPeloConteudo()
    {
        var c = Code;
        if (c.Contains("*** Settings ***") || c.Contains("*** Test Cases ***")) return ".robot";
        if (Regex.IsMatch(c, @"^\s*(SELECT|INSERT|UPDATE|DELETE|WITH|CREATE)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline)) return ".sql";
        if (c.Contains("def ") || c.Contains("import ")) return ".py";
        return ".txt";
    }
}

/// <summary>
/// Encontra blocos de codigo em texto markdown (cercas com tres crases).
///
/// Todo agente do Quorum e instruido a devolver scripts nesse formato; e este
/// extrator que transforma a instrucao em arquivo aproveitavel. Funcao pura: sem
/// IO, testavel isoladamente.
/// </summary>
public static class CodeBlockExtractor
{
    // Cerca de abertura com linguagem opcional, conteudo, cerca de fechamento.
    // Singleline faz o "." casar tambem com quebras de linha.
    private static readonly Regex Cerca = new(
        @"```[ \t]*([A-Za-z0-9_+#-]*)[ \t]*\r?\n(.*?)```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Todos os blocos do texto, na ordem em que aparecem.</summary>
    public static IReadOnlyList<CodeBlock> Extract(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Array.Empty<CodeBlock>();

        var blocos = new List<CodeBlock>();

        foreach (Match m in Cerca.Matches(texto))
        {
            var codigo = m.Groups[2].Value.TrimEnd('\r', '\n');
            if (codigo.Trim().Length == 0) continue;   // cerca vazia nao vira arquivo

            blocos.Add(new CodeBlock(m.Groups[1].Value.Trim(), codigo));
        }

        return blocos;
    }

    /// <summary>
    /// O bloco mais relevante: o ULTIMO do texto.
    ///
    /// A IA costuma mostrar trechos parciais enquanto explica e so no fim entregar
    /// o script completo. Pegar por linguagem (como fazia o app anterior) devolvia
    /// um trecho antigo quando a resposta misturava linguagens; a ordem e um
    /// criterio mais confiavel.
    /// </summary>
    public static CodeBlock? Last(string? texto) => Extract(texto).LastOrDefault();
}
