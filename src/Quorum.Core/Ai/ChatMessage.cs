namespace Quorum.Core.Ai;

/// <summary>Papel de uma mensagem na conversa com a IA.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

/// <summary>
/// Uma mensagem da conversa. Neutra em relacao ao provedor — cada
/// implementacao de <see cref="IAiProvider"/> traduz isto para o formato do seu
/// SDK. Mantem-se simples de proposito: texto + (opcional) chamadas/resultados
/// de ferramenta.
/// </summary>
/// <param name="Role">Quem enviou a mensagem.</param>
/// <param name="Text">Conteudo textual (pode ser vazio quando so ha tool calls).</param>
/// <param name="ToolCalls">Ferramentas que o assistente pediu para executar.</param>
/// <param name="ToolResults">Resultados de ferramentas devolvidos ao assistente.</param>
public sealed record ChatMessage(
    ChatRole Role,
    string Text = "",
    IReadOnlyList<ToolCall>? ToolCalls = null,
    IReadOnlyList<ToolResult>? ToolResults = null)
{
    public static ChatMessage FromUser(string text) => new(ChatRole.User, text);
    public static ChatMessage FromSystem(string text) => new(ChatRole.System, text);
    public static ChatMessage FromAssistant(string text) => new(ChatRole.Assistant, text);
}
