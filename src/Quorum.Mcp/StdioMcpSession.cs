using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Quorum.Core.Ai;

namespace Quorum.Mcp;

/// <summary>
/// Sessao MCP real: sobe o servidor como processo filho (npx) e conversa com ele
/// por stdio, usando o SDK oficial ModelContextProtocol.
///
/// Substitui o que a v4.2 fazia em Python com subprocess e marcadores de texto no
/// stdout. Aqui o protocolo e falado de verdade, com tipos.
/// </summary>
public sealed class StdioMcpSession : IMcpSession
{
    private readonly McpClient _client;
    private readonly Action<string>? _onServerLog;

    private StdioMcpSession(McpClient client, Action<string>? onServerLog)
    {
        _client = client;
        _onServerLog = onServerLog;
    }

    /// <summary>
    /// Sobe o servidor descrito em <paramref name="spec"/> e conclui o handshake.
    /// </summary>
    /// <param name="spec">Qual servidor subir e com quais argumentos.</param>
    /// <param name="onServerLog">
    /// Recebe as linhas que o servidor escreve em stderr — sao mensagens de
    /// progresso, uteis para mostrar na interface enquanto a tarefa roda.
    /// </param>
    public static async Task<StdioMcpSession> StartAsync(
        McpServerSpec spec,
        Action<string>? onServerLog = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var options = new StdioClientTransportOptions
        {
            Command = spec.Command,
            Arguments = spec.Arguments.ToList(),
            Name = spec.DisplayName
        };

        if (onServerLog is not null)
            options.StandardErrorLines = onServerLog;

        var transport = new StdioClientTransport(options);

        try
        {
            var client = await McpClient.CreateAsync(transport, cancellationToken: ct)
                .ConfigureAwait(false);
            return new StdioMcpSession(client, onServerLog);
        }
        catch (Exception ex) when (IsMissingRuntime(ex))
        {
            // A causa mais comum de falha aqui e Node ausente. Dizer isso e melhor
            // que repassar "Win32Exception: No such file or directory".
            throw new McpStartupException(
                $"Nao foi possivel iniciar {spec.DisplayName}. O servidor e distribuido " +
                "via npm e precisa do Node.js 18+ instalado (comando 'npx' disponivel " +
                "no PATH). Instale o Node em nodejs.org e tente de novo.", ex);
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken ct = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);

        return tools.Select(t => new ToolDefinition(
            Name: t.Name,
            Description: Trim(t.Description),
            ParametersJsonSchema: t.JsonSchema.GetRawText())).ToList();
    }

    public async Task<McpToolResponse> CallToolAsync(
        string name, string argumentsJson, CancellationToken ct = default)
    {
        var args = ParseArguments(argumentsJson);

        var result = await _client.CallToolAsync(name, args, cancellationToken: ct)
            .ConfigureAwait(false);

        // O protocolo tem sinal proprio de falha: repassamos como dado, para o
        // executor marcar o ToolResult e a IA receber a falha marcada (e nao
        // precisar deduzi-la do texto).
        return new McpToolResponse(ExtractText(result), result.IsError == true);
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyDictionary<string, object?> ParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, object?>();

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict is null) return new Dictionary<string, object?>();

            return dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        }
        catch (JsonException)
        {
            // Argumento malformado vindo do modelo nao pode derrubar a chamada:
            // manda vazio e deixa o servidor responder o que falta.
            return new Dictionary<string, object?>();
        }
    }

    private static string ExtractText(CallToolResult result)
    {
        var sb = new StringBuilder();

        foreach (var bloco in result.Content)
        {
            if (bloco is TextContentBlock texto)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(texto.Text);
            }
        }

        return sb.Length > 0 ? sb.ToString() : "(a ferramenta nao retornou texto)";
    }

    private static string Trim(string? descricao) =>
        string.IsNullOrEmpty(descricao) ? string.Empty
        : descricao.Length <= 1024 ? descricao
        : descricao[..1024];

    /// <summary>
    /// Reconhece a falha de "executavel nao encontrado", que no Windows e no Linux
    /// chega por caminhos diferentes.
    /// </summary>
    private static bool IsMissingRuntime(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e is System.ComponentModel.Win32Exception) return true;
            if (e is FileNotFoundException) return true;
            if (e.Message.Contains("No such file", StringComparison.OrdinalIgnoreCase)) return true;
            if (e.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase)) return true;
            if (e.InnerException is null) break;
        }
        return false;
    }
}

/// <summary>
/// Falha ao iniciar um servidor MCP, com mensagem que orienta o usuario em vez de
/// expor o erro cru do sistema operacional.
/// </summary>
public sealed class McpStartupException : Exception
{
    public McpStartupException(string message, Exception inner) : base(message, inner) { }
}
