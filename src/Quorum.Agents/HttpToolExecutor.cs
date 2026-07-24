using System.Text;
using System.Text.Json;
using Quorum.Core.Ai;

namespace Quorum.Agents;

/// <summary>
/// Ferramenta HTTP para o agente de API. Nao usa MCP: a requisicao e feita aqui
/// mesmo, com HttpClient. E o equivalente ao _fazer_requisicao_http do Python,
/// agora tipado e testavel.
/// </summary>
public sealed class HttpToolExecutor : IToolExecutor, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly int _maxBodyChars;

    /// <param name="http">
    /// Cliente a usar. Se nulo, um e criado e descartado junto com o executor —
    /// nos testes, passar um cliente com handler falso evita tocar a rede.
    /// </param>
    /// <param name="timeout">Teto por requisicao.</param>
    /// <param name="maxBodyChars">Corte do corpo, para nao estourar o contexto do modelo.</param>
    public HttpToolExecutor(HttpClient? http = null, TimeSpan? timeout = null, int maxBodyChars = 6000)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient();
        if (timeout is { } t) _http.Timeout = t;
        _maxBodyChars = maxBodyChars;
    }

    public const string ToolName = "fazer_requisicao_http";

    public IReadOnlyList<ToolDefinition> Tools { get; } = new[]
    {
        new ToolDefinition(
            ToolName,
            "Executa uma requisicao HTTP e retorna status, headers e corpo da resposta.",
            """
            {
              "type": "object",
              "properties": {
                "metodo":  { "type": "string", "description": "GET, POST, PUT, DELETE, PATCH" },
                "url":     { "type": "string", "description": "URL completa do endpoint" },
                "headers": { "type": "object", "description": "Cabecalhos HTTP (opcional)" },
                "body":    { "type": "string", "description": "Corpo da requisicao como texto (opcional)" }
              },
              "required": ["metodo", "url"]
            }
            """)
    };

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        if (call.Name != ToolName)
            return new ToolResult(call.Id, $"Ferramenta desconhecida: {call.Name}", IsError: true);

        RequestSpec spec;
        try
        {
            spec = RequestSpec.Parse(call.ArgumentsJson);
        }
        catch (Exception ex)
        {
            // Argumentos vieram do MODELO: e ele quem deve corrigir, entao a falha
            // vira resultado de erro em vez de subir como bug (mesma traducao de
            // dominio aplicada no McpToolExecutor).
            return new ToolResult(call.Id, $"Argumentos invalidos: {ex.Message}", IsError: true);
        }

        try
        {
            using var req = spec.ToRequest();
            var inicio = DateTimeOffset.UtcNow;
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var ms = (int)(DateTimeOffset.UtcNow - inicio).TotalMilliseconds;

            var corpo = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (corpo.Length > _maxBodyChars)
                corpo = corpo[.._maxBodyChars] + "\n[corpo truncado]";

            var relatorio = new StringBuilder()
                .AppendLine($"status: {(int)resp.StatusCode} {resp.StatusCode}")
                .AppendLine($"tempo: {ms} ms")
                .AppendLine($"headers: {string.Join(", ", resp.Headers.Select(h => h.Key))}")
                .AppendLine("corpo:")
                .Append(corpo)
                .ToString();

            // Status de erro NAO e falha da ferramenta: a requisicao funcionou e a
            // resposta e justamente o que o teste quer avaliar. Quem julga se 404
            // era esperado e a IA.
            return new ToolResult(call.Id, relatorio);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Rede fora, DNS, TLS, timeout: operacional, a IA precisa saber e reagir.
            return new ToolResult(
                call.Id, $"A requisicao falhou: {ex.GetType().Name}: {ex.Message}", IsError: true);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    /// <summary>Argumentos da chamada, ja validados.</summary>
    private sealed record RequestSpec(string Metodo, string Url, Dictionary<string, string> Headers, string? Body)
    {
        public static RequestSpec Parse(string json)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var raiz = doc.RootElement;

            var metodo = Texto(raiz, "metodo") ?? throw new ArgumentException("'metodo' e obrigatorio.");
            var url = Texto(raiz, "url") ?? throw new ArgumentException("'url' e obrigatoria.");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException($"URL invalida: '{url}'. Use http:// ou https://.");

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (raiz.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Object)
                foreach (var p in h.EnumerateObject())
                    headers[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? "" : p.Value.ToString();

            return new RequestSpec(metodo.ToUpperInvariant(), url, headers, Texto(raiz, "body"));
        }

        private static string? Texto(JsonElement e, string nome) =>
            e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

        public HttpRequestMessage ToRequest()
        {
            var req = new HttpRequestMessage(new HttpMethod(Metodo), Url);

            if (!string.IsNullOrEmpty(Body))
            {
                var tipo = Headers.TryGetValue("Content-Type", out var ct) ? ct : "application/json";
                req.Content = new StringContent(Body, Encoding.UTF8);
                req.Content.Headers.ContentType =
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse(tipo);
            }

            foreach (var (nome, valor) in Headers)
            {
                if (nome.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                req.Headers.TryAddWithoutValidation(nome, valor);
            }

            return req;
        }
    }
}
