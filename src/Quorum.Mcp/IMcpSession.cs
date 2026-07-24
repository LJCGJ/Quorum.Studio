using Quorum.Core.Ai;

namespace Quorum.Mcp;

/// <summary>
/// Resposta de uma ferramenta MCP: o texto e se o SERVIDOR a marcou como erro.
///
/// O protocolo MCP tem um sinal proprio de falha (<c>isError</c> no resultado),
/// distinto de uma excecao no transporte. Carregar esse sinal ate o loop e o que
/// permite marcar o <see cref="ToolResult.IsError"/> corretamente — e, com ele, o
/// adaptador preenche FunctionResultContent.Exception para a IA saber que a
/// ferramenta falhou, em vez de so ler um texto que ela precisa interpretar.
/// </summary>
/// <param name="Text">Conteudo textual devolvido pela ferramenta.</param>
/// <param name="IsError">True quando o proprio servidor sinalizou falha.</param>
public sealed record McpToolResponse(string Text, bool IsError = false);

/// <summary>
/// Uma sessao aberta com um servidor MCP: lista as ferramentas que ele oferece e
/// executa chamadas.
///
/// Existe como INTERFACE de proposito. A implementacao real sobe um processo
/// externo via npx (Playwright, DBHub, MongoDB), o que exige Node instalado — e
/// os runners do CI nao tem. Com esta abstracao, toda a logica que liga o MCP ao
/// loop agentic e testavel com uma sessao falsa, sem Node, sem rede e sem custo.
/// </summary>
public interface IMcpSession : IAsyncDisposable
{
    /// <summary>Ferramentas anunciadas pelo servidor, ja no formato do Quorum.</summary>
    Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken ct = default);

    /// <summary>Executa uma ferramenta no servidor e devolve o texto e o sinal de erro.</summary>
    Task<McpToolResponse> CallToolAsync(string name, string argumentsJson, CancellationToken ct = default);
}

/// <summary>
/// Como subir um servidor MCP: o comando e seus argumentos. Os servidores que o
/// Quorum usa sao distribuidos via npm e executados com npx.
/// </summary>
/// <param name="Command">Executavel (npx.cmd no Windows, npx nos demais).</param>
/// <param name="Arguments">Argumentos, incluindo o pacote do servidor.</param>
/// <param name="DisplayName">Nome legivel, para mensagens ao usuario.</param>
public sealed record McpServerSpec(
    string Command,
    IReadOnlyList<string> Arguments,
    string DisplayName)
{
    /// <summary>
    /// npx tem nome diferente no Windows. Centralizar aqui evita repetir a
    /// checagem em cada servidor (era uma linha duplicada no Python da v4.2).
    /// </summary>
    public static string NpxCommand =>
        OperatingSystem.IsWindows() ? "npx.cmd" : "npx";

    /// <summary>Navegador real via Playwright MCP (Microsoft).</summary>
    /// <param name="headless">Sem janela visivel; util em execucao automatizada.</param>
    public static McpServerSpec Playwright(bool headless = false)
    {
        var args = new List<string> { "-y", "@playwright/mcp@latest" };
        if (headless) args.Add("--headless");
        return new McpServerSpec(NpxCommand, args, "Playwright (navegador)");
    }

    /// <summary>Banco relacional via DBHub.</summary>
    /// <param name="dsn">String de conexao (postgres://, mysql://, sqlite:///...).</param>
    /// <param name="readOnly">Somente leitura: o padrao recomendado.</param>
    public static McpServerSpec DbHub(string dsn, bool readOnly = true)
    {
        var args = new List<string>
        {
            "-y", "@bytebase/dbhub", "--transport", "stdio", "--dsn", dsn
        };
        if (readOnly) args.Add("--readonly");
        return new McpServerSpec(NpxCommand, args, "DBHub (banco de dados)");
    }

    /// <summary>MongoDB via servidor MCP oficial.</summary>
    /// <param name="connectionString">mongodb://usuario:senha@host:porta/banco</param>
    /// <param name="readOnly">
    /// Somente leitura. ATENCAO: o padrao do servidor oficial e leitura E escrita,
    /// entao a flag precisa ser passada explicitamente para restringir.
    /// </param>
    public static McpServerSpec MongoDb(string connectionString, bool readOnly = true)
    {
        var args = new List<string>
        {
            "-y", "mongodb-mcp-server@latest", "--connectionString", connectionString
        };
        if (readOnly) args.Add("--readOnly");
        return new McpServerSpec(NpxCommand, args, "MongoDB");
    }
}
