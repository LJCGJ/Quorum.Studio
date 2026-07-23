namespace Quorum.Core.Ai;

/// <summary>Parametros de uma chamada a IA.</summary>
/// <param name="ModelId">Modelo a usar (Id exato do provedor).</param>
/// <param name="Messages">Historico da conversa, em ordem.</param>
/// <param name="Tools">Ferramentas disponiveis (vazio = conversa pura).</param>
/// <param name="SystemPrompt">
/// Instrucao de sistema. Fica em campo proprio porque cada provedor a entrega de
/// um jeito (Claude: parametro system; OpenAI: mensagem role=system; Gemini:
/// system_instruction). Centralizar aqui evita o bug da v4.2 em que o Gemini
/// ficava sem system prompt.
/// </param>
/// <param name="MaxTokens">Teto de tokens da resposta.</param>
public sealed record CompletionRequest(
    string ModelId,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    string? SystemPrompt = null,
    int MaxTokens = 2048);

/// <summary>
/// Contagem de tokens de uma chamada, quando o provedor a informa. Serve para
/// medir custo real por tarefa — insumo do modo economia e de um futuro medidor
/// na interface. Campos anulaveis porque nem todo provedor reporta tudo.
/// </summary>
/// <param name="InputTokens">Tokens de entrada (prompt + historico + ferramentas).</param>
/// <param name="OutputTokens">Tokens gerados na resposta.</param>
public sealed record TokenUsage(long? InputTokens, long? OutputTokens)
{
    /// <summary>Total, quando ao menos um lado e conhecido.</summary>
    public long? Total => (InputTokens, OutputTokens) switch
    {
        (null, null) => null,
        var (i, o) => (i ?? 0) + (o ?? 0)
    };

    /// <summary>Uso vazio (provedor nao reportou).</summary>
    public static TokenUsage Unknown { get; } = new(null, null);
}

/// <summary>Resposta de uma chamada a IA.</summary>
/// <param name="Text">Texto produzido (pode ser vazio quando so ha tool calls).</param>
/// <param name="ToolCalls">Ferramentas que a IA pediu para executar.</param>
/// <param name="Usage">Tokens consumidos, se o provedor informou.</param>
/// <param name="Truncated">
/// True quando a resposta foi cortada por atingir o teto de tokens (o modelo
/// tinha mais a dizer). Sinaliza que o texto pode estar incompleto.
/// </param>
public sealed record CompletionResponse(
    string Text,
    IReadOnlyList<ToolCall> ToolCalls,
    TokenUsage? Usage = null,
    bool Truncated = false)
{
    /// <summary>Se a IA pediu ao menos uma ferramenta (loop agentic continua).</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;
}

/// <summary>
/// Contrato de um provedor de IA (Claude, OpenAI, Gemini). Uma unica operacao:
/// dado o historico + ferramentas, produzir a proxima resposta. O loop agentic
/// (em Quorum.Agents) fica por cima disto e nao conhece o SDK concreto — e o que
/// permite testar o loop com um provider falso, sem gastar creditos.
///
/// Herda IDisposable porque as implementacoes reais mantem um HttpClient vivo; o
/// consumidor descarta o provider sem precisar conhecer o tipo concreto.
/// </summary>
public interface IAiProvider : IDisposable
{
    /// <summary>Provedor que esta implementacao atende.</summary>
    Quorum.Core.Models.AiProvider Provider { get; }

    /// <summary>Produz a proxima resposta da IA para a requisicao dada.</summary>
    Task<CompletionResponse> CompleteAsync(
        CompletionRequest request,
        CancellationToken cancellationToken = default);
}
