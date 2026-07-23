using Quorum.Core.Models;

namespace Quorum.Core.Routing;

/// <summary>
/// Roteador multi-IA. Dada uma tarefa e as preferencias do usuario, escolhe o
/// modelo mais adequado do <see cref="ModelRegistry"/>.
///
/// Projetado como funcao pura (sem IO, sem estado mutavel): a mesma entrada
/// produz sempre a mesma saida, entao cada regra vira um teste unitario e a
/// logica pode ser validada sem gastar creditos de IA.
///
/// Regras, em ordem:
///   1. Pin do usuario: se ele fixou um modelo valido que atende os requisitos
///      obrigatorios da tarefa (ex.: tool-use), usa esse.
///   2. Filtra candidatos que atendem os requisitos OBRIGATORIOS (tool-use e,
///      quando estimado, janela de contexto suficiente).
///   3. Escolhe o tier-alvo pela natureza da tarefa.
///   4. Entre os candidatos, ordena por adequacao (proximidade do tier-alvo,
///      economia, provedor preferido, custo) e devolve o melhor.
/// </summary>
public sealed class ModelRouter
{
    private readonly ModelRegistry _registry;

    public ModelRouter(ModelRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public RoutingResult Route(TaskDescriptor task, RoutingPreferences prefs)
    {
        prefs ??= RoutingPreferences.Default;

        // 1. Override explicito do usuario
        if (!string.IsNullOrWhiteSpace(prefs.PinnedModelId))
        {
            var pinned = _registry.FindById(prefs.PinnedModelId!);
            if (pinned is null)
                return RoutingResult.Fail(
                    $"O modelo fixado '{prefs.PinnedModelId}' nao esta na lista atual. " +
                    "Ele pode ter sido aposentado — atualize a lista de modelos.");
            if (task.NeedsTools && !pinned.SupportsTools)
                return RoutingResult.Fail(
                    $"O modelo fixado '{pinned.Id}' nao suporta ferramentas, mas esta " +
                    "tarefa exige automacao. Escolha outro modelo ou remova a fixacao.");
            if (task.EstimatedContextTokens > 0 &&
                pinned.ContextWindow < task.EstimatedContextTokens)
                return RoutingResult.Fail(
                    $"O modelo fixado '{pinned.Id}' tem janela de {pinned.ContextWindow:N0} " +
                    $"tokens, menor que os ~{task.EstimatedContextTokens:N0} desta tarefa. " +
                    "Escolha um modelo de janela maior ou remova a fixacao.");
            return RoutingResult.Ok(pinned);
        }

        // 2. Requisitos obrigatorios
        var candidates = _registry.All.Where(m => AttendsHardRequirements(m, task)).ToList();
        if (candidates.Count == 0)
        {
            var motivo = task.NeedsTools
                ? "Nenhum modelo disponivel suporta ferramentas (necessario para automacao). "
                : "Nenhum modelo disponivel atende aos requisitos da tarefa. ";
            return RoutingResult.Fail(motivo + "Verifique se ha uma chave de API valida " +
                                      "e se a lista de modelos foi carregada.");
        }

        // 3. Tier-alvo pela tarefa
        var targetTier = TargetTierFor(task);

        // 4. Ordena por adequacao e devolve o melhor
        var best = candidates
            .OrderBy(m => TierDistance(m.Tier, targetTier, prefs.EconomyMode))
            .ThenByDescending(m => prefs.PreferredProvider.HasValue &&
                                   m.Provider == prefs.PreferredProvider.Value)
            .ThenBy(m => m.CostInputPerMillion + m.CostOutputPerMillion)
            .ThenBy(m => m.Id, StringComparer.Ordinal) // desempate estavel
            .First();

        return RoutingResult.Ok(best);
    }

    /// <summary>
    /// Constroi a cadeia de fallback: o modelo ideal primeiro, seguido dos demais
    /// candidatos validos em ordem de adequacao. Serve para o agente tentar o
    /// proximo quando um provedor falha ou estoura cota (resiliencia da secao 5.3).
    ///
    /// CONTRATO: cadeia VAZIA significa que nenhum modelo serve (ex.: pin
    /// invalido ou nenhum candidato atende aos requisitos). Nesse caso o chamador
    /// deve invocar <see cref="Route"/> com os mesmos argumentos para obter a
    /// razao legivel da falha e exibi-la ao usuario.
    /// </summary>
    public IReadOnlyList<ModelInfo> RouteWithFallback(TaskDescriptor task, RoutingPreferences prefs)
    {
        prefs ??= RoutingPreferences.Default;

        // Com pin valido, a cadeia e so o modelo fixado (respeita a vontade do usuario).
        if (!string.IsNullOrWhiteSpace(prefs.PinnedModelId))
        {
            var r = Route(task, prefs);
            return r.Success ? new[] { r.Selected! } : Array.Empty<ModelInfo>();
        }

        var targetTier = TargetTierFor(task);
        return _registry.All
            .Where(m => AttendsHardRequirements(m, task))
            .OrderBy(m => TierDistance(m.Tier, targetTier, prefs.EconomyMode))
            .ThenByDescending(m => prefs.PreferredProvider.HasValue &&
                                   m.Provider == prefs.PreferredProvider.Value)
            .ThenBy(m => m.CostInputPerMillion + m.CostOutputPerMillion)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool AttendsHardRequirements(ModelInfo m, TaskDescriptor task)
    {
        if (task.NeedsTools && !m.SupportsTools) return false;
        if (task.EstimatedContextTokens > 0 && m.ContextWindow < task.EstimatedContextTokens)
            return false;
        return true;
    }

    private static ModelTier TargetTierFor(TaskDescriptor task) => task.Kind switch
    {
        TaskKind.Chat => ModelTier.Fast,
        TaskKind.DomScan => ModelTier.Fast,
        TaskKind.Automation => ModelTier.Balanced,
        TaskKind.Analysis => ModelTier.Powerful,
        _ => ModelTier.Balanced
    };

    /// <summary>
    /// Distancia entre o tier de um modelo e o tier-alvo. Menor = melhor.
    /// Em modo economia, o custo domina: o roteador prefere o tier mais BAIXO que
    /// ainda atenda os requisitos obrigatorios, mesmo que a tarefa sugira um tier
    /// mais alto. Fora da economia, vale a proximidade do tier-alvo.
    /// </summary>
    private static int TierDistance(ModelTier modelTier, ModelTier target, bool economy)
    {
        if (economy)
            return (int)modelTier;   // quanto mais baixo o tier, melhor (mais barato)

        return Math.Abs((int)modelTier - (int)target);
    }
}
