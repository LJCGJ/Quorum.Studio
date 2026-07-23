using Quorum.Core.Ai;

namespace Quorum.Agents;

/// <summary>
/// Executa uma ferramenta pedida pela IA e devolve o resultado. Cada agente
/// concreto (Tela, API, Banco...) implementa isto ligando as ferramentas ao seu
/// mundo real: o ScreenAgent encaminha para o MCP do Playwright, o ApiAgent faz
/// a requisicao HTTP, e assim por diante.
///
/// Separar a EXECUCAO das ferramentas do LOOP (AgentLoop) e o que torna o loop
/// generico e testavel: nos testes, um executor falso responde sem tocar em
/// navegador, banco ou rede.
/// </summary>
public interface IToolExecutor
{
    /// <summary>Definicoes das ferramentas que este executor oferece a IA.</summary>
    IReadOnlyList<ToolDefinition> Tools { get; }

    /// <summary>Executa uma chamada de ferramenta e devolve o resultado.</summary>
    Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default);
}
