using Quorum.Agents;
using Quorum.Core.Ai;

namespace Quorum.Mcp;

/// <summary>
/// Liga um servidor MCP ao loop agentic: expoe as ferramentas do servidor a IA e
/// encaminha as chamadas dela.
///
/// Traduz as falhas do SEU dominio antes que cheguem ao loop — como combinado no
/// design dos agentes concretos. Um servidor MCP pode lancar
/// <see cref="InvalidOperationException"/> para coisas operacionais (sessao
/// encerrada, ferramenta indisponivel), e o classificador generico trataria isso
/// como bug e derrubaria a tarefa. Aqui essas falhas viram
/// <see cref="ToolResult"/> de erro, que a IA ve e sobre o qual pode reagir.
/// </summary>
public sealed class McpToolExecutor : IToolExecutor
{
    private readonly IMcpSession _session;

    private McpToolExecutor(IMcpSession session, IReadOnlyList<ToolDefinition> tools)
    {
        _session = session;
        Tools = tools;
    }

    public IReadOnlyList<ToolDefinition> Tools { get; }

    /// <summary>
    /// Abre o executor consultando as ferramentas do servidor. Fabrica assincrona
    /// porque listar ferramentas exige ida ao servidor — o que nao cabe num
    /// construtor.
    /// </summary>
    public static async Task<McpToolExecutor> CreateAsync(
        IMcpSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var tools = await session.ListToolsAsync(ct).ConfigureAwait(false);
        return new McpToolExecutor(session, tools);
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        try
        {
            var resposta = await _session
                .CallToolAsync(call.Name, call.ArgumentsJson, ct)
                .ConfigureAwait(false);

            // Erro sinalizado PELO PROTOCOLO (isError no resultado) tambem marca o
            // ToolResult — sem isso, o adaptador nao preencheria
            // FunctionResultContent.Exception e a IA teria de deduzir a falha lendo
            // o texto. Falha e falha, venha de excecao ou do proprio servidor.
            return new ToolResult(call.Id, resposta.Text, resposta.IsError);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelamento real do usuario: deixa subir para o loop encerrar limpo.
            throw;
        }
        catch (Exception ex) when (IsOperational(ex))
        {
            // Falha do servidor MCP ou da ferramenta: e informacao util para a IA,
            // nao um defeito do Quorum. Ela recebe o erro e pode tentar outro
            // caminho (ex.: o seletor nao existe, entao tirar um snapshot antes).
            return new ToolResult(
                call.Id,
                $"A ferramenta '{call.Name}' falhou: {ex.Message}",
                IsError: true);
        }
    }

    /// <summary>
    /// Falhas esperadas ao operar um servidor externo.
    ///
    /// DIVERGENCIA INTENCIONAL de <c>AiFailureClassifier</c>: la,
    /// <see cref="ArgumentException"/> e <see cref="InvalidOperationException"/>
    /// contam como bug e sobem; AQUI elas viram erro para a IA. O motivo e a
    /// origem: numa chamada MCP, argumento invalido normalmente veio do MODELO
    /// (ele montou os parametros), e sessao/ferramenta indisponivel e condicao
    /// operacional do servidor externo. Nos dois casos quem deve ver e corrigir e
    /// a IA, nao o desenvolvedor.
    ///
    /// Nao "corrija" esta lista para alinhar com o classificador generico: e a
    /// traducao de dominio que ele espera que cada executor concreto faca.
    /// Bugs de verdade (referencia nula, cast invalido) e fatais continuam subindo.
    /// </summary>
    private static bool IsOperational(Exception ex) => ex switch
    {
        NullReferenceException => false,
        InvalidCastException => false,
        IndexOutOfRangeException => false,
        OutOfMemoryException => false,
        _ => true
    };
}
