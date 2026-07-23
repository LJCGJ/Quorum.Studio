namespace Quorum.Agents;

/// <summary>
/// Classifica excecoes da chamada a IA: uma FALHA OPERACIONAL (rede caiu, API
/// recusou a chave, cota estourada, timeout) justifica encerrar com
/// <see cref="AgentStopReason.Failed"/> e tentar o proximo modelo da cadeia.
///
/// Ja excecoes FATAIS do runtime (falta de memoria, stack overflow) e erros de
/// PROGRAMACAO (NullReference, InvalidCast, argumento invalido) NAO devem virar
/// fallback: tentar outro modelo apenas mascara o bug — todas as tentativas
/// falhariam igual, e o usuario veria "todos os modelos falharam" no lugar do
/// erro real. Essas sobem para quem chama.
/// </summary>
internal static class AiFailureClassifier
{
    /// <summary>
    /// True se a excecao representa uma falha operacional recuperavel por
    /// fallback; false se deve ser propagada (bug ou erro fatal).
    /// </summary>
    public static bool IsRecoverable(Exception ex) => ex switch
    {
        // Fatais do runtime: nunca mascarar.
        OutOfMemoryException => false,
        StackOverflowException => false,

        // Erros de programacao: sao bugs nossos, devem aparecer, nao virar fallback.
        NullReferenceException => false,
        ArgumentException => false,          // inclui ArgumentNullException
        InvalidCastException => false,
        IndexOutOfRangeException => false,
        InvalidOperationException => false,  // uso indevido de API interna
        NotSupportedException => false,      // ex.: QuorumDeclaredFunction invocada
                                             // indevidamente, ou recurso nao suportado
                                             // pelo SDK — erro de config que deve aparecer

        // Todo o resto (HttpRequestException, TaskCanceledException por timeout,
        // excecoes dos SDKs de IA para 401/429/5xx, etc.) e tratado como falha
        // operacional: encerra com Failed e permite tentar o proximo modelo.
        _ => true
    };
}
