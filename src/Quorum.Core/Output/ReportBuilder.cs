using System.Net;
using System.Text;

namespace Quorum.Core.Output;

/// <summary>Dados de um relatorio a exportar.</summary>
/// <param name="Title">Titulo do documento.</param>
/// <param name="Subtitle">Uma linha de contexto.</param>
/// <param name="Body">Conteudo principal (texto corrido do relatorio).</param>
/// <param name="Model">Modelo que produziu o resultado, se conhecido.</param>
/// <param name="Tokens">Tokens consumidos, se conhecidos.</param>
/// <param name="Operator">Quem executou (nome da conta do sistema).</param>
public sealed record ReportData(
    string Title,
    string Subtitle,
    string Body,
    string? Model = null,
    long? Tokens = null,
    string? Operator = null);

/// <summary>
/// Monta o HTML de um relatorio de teste, pronto para arquivar ou compartilhar.
///
/// Funcao pura (texto entra, texto sai): quem escreve em disco e a interface. Isso
/// mantem o Core sem IO e o gerador testavel sem tocar no sistema de arquivos.
/// </summary>
public static class ReportBuilder
{
    public static string BuildHtml(ReportData dados, DateTimeOffset? momento = null)
    {
        ArgumentNullException.ThrowIfNull(dados);
        var quando = momento ?? DateTimeOffset.Now;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"pt-br\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{Esc(dados.Title)} — Quorum</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class=\"folha\">");
        sb.AppendLine("  <header>");
        sb.AppendLine($"    <h1>{Esc(dados.Title)}</h1>");
        sb.AppendLine($"    <p class=\"sub\">{Esc(dados.Subtitle)}</p>");
        sb.AppendLine("  </header>");

        // Ficha: so mostra o que existe, sem campos vazios
        sb.AppendLine("  <dl class=\"ficha\">");
        Campo(sb, "Data", quando.ToString("dd/MM/yyyy HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(dados.Operator)) Campo(sb, "Operador", dados.Operator!);
        if (!string.IsNullOrWhiteSpace(dados.Model)) Campo(sb, "Modelo", dados.Model!);
        if (dados.Tokens is { } t) Campo(sb, "Tokens", t.ToString("N0"));
        sb.AppendLine("  </dl>");

        // Corpo: blocos de codigo saem destacados do texto corrido
        sb.AppendLine("  <main>");
        EscreverCorpo(sb, dados.Body);
        sb.AppendLine("  </main>");

        sb.AppendLine("  <footer>Gerado pelo Quorum · QA · AI Test Automation</footer>");
        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }

    /// <summary>
    /// Escreve o corpo separando prosa de codigo: um relatorio com o script
    /// misturado ao texto e dificil de aproveitar.
    /// </summary>
    private static void EscreverCorpo(StringBuilder sb, string corpo)
    {
        var blocos = CodeBlockExtractor.Extract(corpo);
        if (blocos.Count == 0)
        {
            sb.AppendLine($"    <pre class=\"texto\">{Esc(corpo)}</pre>");
            return;
        }

        var restante = corpo;
        foreach (var bloco in blocos)
        {
            var marca = "```";
            var idx = restante.IndexOf(marca, StringComparison.Ordinal);
            if (idx > 0)
            {
                var antes = restante[..idx].Trim();
                if (antes.Length > 0)
                    sb.AppendLine($"    <pre class=\"texto\">{Esc(antes)}</pre>");
            }

            sb.AppendLine($"    <div class=\"codigo\"><span class=\"lang\">{Esc(bloco.DisplayLanguage)}</span>");
            sb.AppendLine($"      <pre>{Esc(bloco.Code)}</pre></div>");

            var fim = restante.IndexOf(marca, idx + marca.Length, StringComparison.Ordinal);
            var fechamento = fim >= 0
                ? restante.IndexOf(marca, fim + marca.Length, StringComparison.Ordinal)
                : -1;
            restante = fechamento >= 0
                ? restante[(fechamento + marca.Length)..]
                : string.Empty;
        }

        var sobra = restante.Trim();
        if (sobra.Length > 0)
            sb.AppendLine($"    <pre class=\"texto\">{Esc(sobra)}</pre>");
    }

    private static void Campo(StringBuilder sb, string rotulo, string valor)
    {
        sb.AppendLine($"    <dt>{Esc(rotulo)}</dt><dd>{Esc(valor)}</dd>");
    }

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    // Paleta da marca, para o relatorio ter a cara do produto.
    private const string Css = """
        body{margin:0;background:#f4f6fa;color:#0f172a;
             font-family:'Segoe UI',Inter,Arial,sans-serif;font-size:14px;line-height:1.55}
        .folha{max-width:900px;margin:32px auto;background:#fff;border-radius:12px;
               overflow:hidden;box-shadow:0 2px 18px rgba(11,18,32,.10)}
        header{background:#0b1220;color:#f1f5f9;padding:26px 34px}
        header h1{margin:0;font-size:22px;font-weight:600}
        header .sub{margin:6px 0 0;font-size:13px;color:#38bdf8}
        .ficha{display:grid;grid-template-columns:auto 1fr;gap:4px 18px;margin:0;
               padding:16px 34px;background:#eef2f8;border-bottom:1px solid #dce3ed;font-size:13px}
        .ficha dt{color:#64748b}
        .ficha dd{margin:0;color:#0f172a}
        main{padding:24px 34px}
        pre.texto{white-space:pre-wrap;word-wrap:break-word;margin:0 0 18px;font-family:inherit}
        .codigo{position:relative;margin:0 0 20px;border:1px solid #dce3ed;border-radius:8px;
                background:#0b1220;overflow:hidden}
        .codigo .lang{display:block;padding:6px 14px;background:#1b2a4a;color:#34d399;
                      font-size:11px;letter-spacing:.08em;text-transform:uppercase}
        .codigo pre{margin:0;padding:14px;color:#e2e8f0;overflow-x:auto;
                    font-family:Consolas,'Courier New',monospace;font-size:12.5px}
        footer{padding:16px 34px;border-top:1px solid #eef2f8;color:#94a3b8;
               font-size:12px;text-align:center}
        """;
}
