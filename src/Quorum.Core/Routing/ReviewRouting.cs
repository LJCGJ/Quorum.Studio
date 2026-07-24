using Quorum.Core.Models;

namespace Quorum.Core.Routing;

/// <summary>
/// Escolhe quem revisa o resultado de uma tarefa.
///
/// A revisao e o "quorum" que da nome ao produto: uma segunda IA lendo
/// criticamente o que a primeira produziu. Para valer alguma coisa, o revisor
/// precisa ser DIFERENTE — um modelo revisando o proprio texto tende a concordar
/// consigo mesmo, e o usuario pagaria por uma confirmacao vazia.
///
/// Ordem de preferencia:
///   1. outro PROVEDOR (opiniao de fato independente)
///   2. dentro disso, modelo mais capaz (revisao pede raciocinio, nao velocidade)
///   3. se so ha um provedor, outro MODELO do mesmo
///   4. se so ha um modelo no total, nao ha revisao possivel
/// </summary>
public static class ReviewRouting
{
    /// <summary>
    /// Candidatos a revisor, do melhor para o pior. Vazio quando nao ha ninguem
    /// diferente do autor para revisar.
    /// </summary>
    /// <param name="registry">Catalogo atual.</param>
    /// <param name="prefs">Preferencias (usa apenas os provedores com chave).</param>
    /// <param name="originalModelId">Modelo que produziu o resultado a revisar.</param>
    public static IReadOnlyList<ModelInfo> SelectReviewers(
        ModelRegistry registry, RoutingPreferences prefs, string? originalModelId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        prefs ??= RoutingPreferences.Default;

        var original = originalModelId is null ? null : registry.FindById(originalModelId);

        // A revisao e analise de texto: nao precisa de ferramentas, entao modelos
        // sem tool-use tambem servem e podem ser mais baratos.
        var candidatos = registry.All
            .Where(m => prefs.IsUsable(m.Provider))
            .Where(m => m.Id != originalModelId)
            .ToList();

        if (candidatos.Count == 0) return Array.Empty<ModelInfo>();

        return candidatos
            .OrderByDescending(m => original is null || m.Provider != original.Provider)
            .ThenByDescending(m => (int)m.Tier)
            .ThenBy(m => m.PricingKnown ? 0 : 1)
            .ThenBy(m => m.CostInputPerMillion + m.CostOutputPerMillion)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// True quando o revisor escolhido vem de outro provedor — a interface usa isso
    /// para dizer se a segunda opiniao e realmente independente.
    /// </summary>
    public static bool IsIndependent(ModelRegistry registry, string? originalModelId, ModelInfo reviewer)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(reviewer);

        var original = originalModelId is null ? null : registry.FindById(originalModelId);
        return original is null || original.Provider != reviewer.Provider;
    }
}
