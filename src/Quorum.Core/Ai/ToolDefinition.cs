namespace Quorum.Core.Ai;

/// <summary>
/// Definicao de uma ferramenta exposta a IA (nome, descricao e schema JSON dos
/// parametros). Neutra em relacao ao provedor — cada provider a converte para o
/// formato do seu SDK (input_schema no Claude, parameters no OpenAI/Gemini).
/// Na v4.2 essa conversao estava triplicada; aqui a definicao e unica.
/// </summary>
/// <param name="Name">Nome da ferramenta.</param>
/// <param name="Description">O que a ferramenta faz (ajuda a IA a decidir usa-la).</param>
/// <param name="ParametersJsonSchema">JSON Schema dos parametros, como string.</param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema);

/// <summary>
/// Um pedido da IA para executar uma ferramenta. O <see cref="Id"/> correlaciona
/// o pedido ao seu <see cref="ToolResult"/> (exigido por Claude e OpenAI).
/// </summary>
/// <param name="Id">Identificador do pedido, ecoado no resultado.</param>
/// <param name="Name">Ferramenta pedida.</param>
/// <param name="ArgumentsJson">Argumentos como JSON (objeto).</param>
public sealed record ToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

/// <summary>Resultado da execucao de uma ferramenta, devolvido a IA.</summary>
/// <param name="ToolCallId">Id do <see cref="ToolCall"/> correspondente.</param>
/// <param name="Content">Conteudo textual do resultado (ja truncado, se preciso).</param>
/// <param name="IsError">Se a execucao falhou (a IA pode reagir a isso).</param>
public sealed record ToolResult(
    string ToolCallId,
    string Content,
    bool IsError = false);
