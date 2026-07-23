namespace Quorum.Agents;

/// <summary>
/// Limites do loop agentic (guardrails de custo herdados da v4.2). Imutavel.
/// </summary>
/// <param name="MaxSteps">
/// Teto de iteracoes IA↔ferramenta. Cada passo custa tokens; este e o principal
/// controle de gasto por tarefa (era MAX_ITERACOES no Python).
/// </param>
/// <param name="MaxToolResultChars">
/// Corta o texto de cada resultado de ferramenta para nao estourar o contexto do
/// modelo (era o [:8000]/[:6000] espalhado pelo Python).
/// </param>
/// <param name="MaxTokens">Teto de tokens por resposta do modelo (era MAX_TOKENS).</param>
public sealed record AgentLoopOptions(
    int MaxSteps = 15,
    int MaxToolResultChars = 8000,
    int MaxTokens = 2048)
{
    public static AgentLoopOptions Default { get; } = new();
}

/// <summary>Por que o loop terminou.</summary>
public enum AgentStopReason
{
    /// <summary>A IA respondeu sem pedir ferramentas: tarefa concluida.</summary>
    Completed,

    /// <summary>Atingiu o teto de passos antes de concluir.</summary>
    StepLimitReached,

    /// <summary>Cancelado (usuario ou timeout).</summary>
    Cancelled,

    /// <summary>
    /// A chamada a IA falhou de forma nao recuperavel (chave invalida, sem rede,
    /// cota estourada). O <c>FinalText</c> carrega a razao legivel. Substitui o
    /// tratamento espalhado de ResourceExhausted/erros que a v4.2 tinha.
    /// </summary>
    Failed
}

/// <summary>Resultado final do loop agentic.</summary>
/// <param name="FinalText">Texto final produzido pela IA (o relatorio).</param>
/// <param name="StopReason">Motivo do encerramento.</param>
/// <param name="StepsUsed">Quantos passos foram gastos.</param>
/// <param name="TotalTokens">
/// Soma dos tokens consumidos em todos os passos (quando os provedores informam).
/// Nulo se nenhum passo reportou uso. Insumo direto para medir custo por tarefa.
/// </param>
/// <param name="OutputTruncated">
/// True se a resposta FINAL foi cortada por atingir o teto de tokens — o relatorio
/// pode estar incompleto e a interface deve avisar o usuario.
/// </param>
public sealed record AgentResult(
    string FinalText,
    AgentStopReason StopReason,
    int StepsUsed,
    long? TotalTokens = null,
    bool OutputTruncated = false);
