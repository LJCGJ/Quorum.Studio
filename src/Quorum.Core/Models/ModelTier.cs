namespace Quorum.Core.Models;

/// <summary>
/// Faixa de capacidade x custo de um modelo. O roteador escolhe o tier de acordo
/// com a tarefa; dentro de um tier, o provedor concreto pode variar (fallback).
/// A ordem importa: valores maiores = mais capaz e mais caro.
/// </summary>
public enum ModelTier
{
    /// <summary>Barato e rapido. Bom para conversa e tarefas simples (Haiku, Flash, mini).</summary>
    Fast = 0,

    /// <summary>Equilibrio custo/capacidade. Padrao para automacao com ferramentas.</summary>
    Balanced = 1,

    /// <summary>Mais capaz e mais caro. Analise complexa, revisao critica (Opus, Pro).</summary>
    Powerful = 2
}
