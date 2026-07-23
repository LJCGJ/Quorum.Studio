namespace Quorum.Core.Models;

/// <summary>
/// Natureza da tarefa que sera enviada a uma IA. O roteador usa isto (junto com
/// as preferencias do usuario) para escolher o tier e o modelo adequados.
/// </summary>
public enum TaskKind
{
    /// <summary>Conversa livre, planejamento. Sem ferramentas. Tier barato basta.</summary>
    Chat,

    /// <summary>Leitura estatica do DOM. Sem tool-use externo, mas pode ter contexto grande.</summary>
    DomScan,

    /// <summary>Automacao com ferramentas (Tela, API, Banco). Exige SupportsTools.</summary>
    Automation,

    /// <summary>Analise/revisao de um relatorio ou log longo. Prioriza janela de contexto.</summary>
    Analysis
}

/// <summary>
/// Descritor da tarefa a rotear. Imutavel e sem dependencia de IO — o roteador
/// e uma funcao pura sobre este descritor, o que o torna trivial de testar.
/// </summary>
/// <param name="Kind">Natureza da tarefa.</param>
/// <param name="RequiresTools">
/// Se a tarefa precisa de tool-use. Normalmente derivado de <see cref="Kind"/>,
/// mas exposto para casos em que o chamador sabe mais que o tipo sugere.
/// </param>
/// <param name="EstimatedContextTokens">
/// Estimativa do tamanho do contexto (para escolher janela). 0 = desconhecido.
/// </param>
public sealed record TaskDescriptor(
    TaskKind Kind,
    bool RequiresTools = false,
    int EstimatedContextTokens = 0)
{
    /// <summary>
    /// Indica se a tarefa exige, na pratica, um modelo com tool-use — seja pelo
    /// tipo (Automation) ou por pedido explicito do chamador.
    /// </summary>
    public bool NeedsTools => RequiresTools || Kind == TaskKind.Automation;
}
