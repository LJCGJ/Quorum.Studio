using Quorum.Agents;
using Quorum.Core.Models;

namespace Quorum.Mcp;

/// <summary>
/// Base dos agentes que rodam sobre um servidor MCP. Cuida do ciclo de vida
/// (subir o servidor, descobrir ferramentas, encerrar) para que cada agente
/// concreto so precise trazer o prompt do seu dominio.
/// </summary>
public sealed class McpBackedAgent : IQuorumAgent
{
    private readonly IMcpSession _session;

    private McpBackedAgent(
        string displayName, string systemPrompt, TaskKind taskKind,
        IMcpSession session, IToolExecutor tools)
    {
        DisplayName = displayName;
        SystemPrompt = systemPrompt;
        TaskKind = taskKind;
        _session = session;
        Tools = tools;
    }

    public string DisplayName { get; }
    public string SystemPrompt { get; }
    public IToolExecutor Tools { get; }
    public TaskKind TaskKind { get; }

    /// <summary>
    /// Sobe o servidor, descobre as ferramentas e devolve o agente pronto.
    /// A sessao e encerrada no <see cref="DisposeAsync"/> — use com
    /// <c>await using</c> para o processo do servidor nao ficar orfao.
    /// </summary>
    public static async Task<McpBackedAgent> CreateAsync(
        string displayName,
        string systemPrompt,
        McpServerSpec spec,
        Action<string>? onServerLog = null,
        TaskKind taskKind = TaskKind.Automation,
        CancellationToken ct = default)
    {
        var session = await StdioMcpSession.StartAsync(spec, onServerLog, ct).ConfigureAwait(false);
        try
        {
            var tools = await McpToolExecutor.CreateAsync(session, ct).ConfigureAwait(false);
            return new McpBackedAgent(displayName, systemPrompt, taskKind, session, tools);
        }
        catch
        {
            // Falhou depois de subir o servidor: encerra o processo antes de propagar,
            // senao ele fica rodando sem dono.
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Construtor para testes: injeta uma sessao ja pronta (sem Node).</summary>
    public static async Task<McpBackedAgent> FromSessionAsync(
        string displayName, string systemPrompt, IMcpSession session,
        TaskKind taskKind = TaskKind.Automation, CancellationToken ct = default)
    {
        var tools = await McpToolExecutor.CreateAsync(session, ct).ConfigureAwait(false);
        return new McpBackedAgent(displayName, systemPrompt, taskKind, session, tools);
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
