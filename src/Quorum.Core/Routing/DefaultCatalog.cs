using Quorum.Core.Models;

namespace Quorum.Core.Routing;

/// <summary>
/// Catalogo semente com modelos atuais dos tres provedores. Serve como fallback
/// inicial ANTES de a lista real ser buscada no provedor (o registro dinamico
/// substitui isto assim que uma chave valida esta disponivel).
///
/// IMPORTANTE: esta lista existe so para o app ter algo utilizavel na primeira
/// execucao. A fonte de verdade e sempre o provedor — por isso os custos sao
/// aproximados e a lista nao precisa ser exaustiva. Precos em USD por milhao de
/// tokens, valores de referencia.
/// </summary>
public static class DefaultCatalog
{
    public static IReadOnlyList<ModelInfo> Models { get; } = new[]
    {
        // --- Claude ---
        new ModelInfo(AiProvider.Claude, "claude-haiku-4-5-20251001", "Claude Haiku 4.5",
            ModelTier.Fast, SupportsTools: true, ContextWindow: 200_000,
            CostInputPerMillion: 1m, CostOutputPerMillion: 5m),
        new ModelInfo(AiProvider.Claude, "claude-sonnet-4-6", "Claude Sonnet 4.6",
            ModelTier.Balanced, SupportsTools: true, ContextWindow: 200_000,
            CostInputPerMillion: 3m, CostOutputPerMillion: 15m),
        new ModelInfo(AiProvider.Claude, "claude-opus-4-8", "Claude Opus 4.8",
            ModelTier.Powerful, SupportsTools: true, ContextWindow: 200_000,
            CostInputPerMillion: 5m, CostOutputPerMillion: 25m),

        // --- OpenAI ---
        new ModelInfo(AiProvider.OpenAI, "gpt-4o-mini", "GPT-4o mini",
            ModelTier.Fast, SupportsTools: true, ContextWindow: 128_000,
            CostInputPerMillion: 0.15m, CostOutputPerMillion: 0.60m),
        new ModelInfo(AiProvider.OpenAI, "gpt-4o", "GPT-4o",
            ModelTier.Balanced, SupportsTools: true, ContextWindow: 128_000,
            CostInputPerMillion: 2.50m, CostOutputPerMillion: 10m),

        // --- Gemini ---
        new ModelInfo(AiProvider.Gemini, "gemini-2.5-flash-lite", "Gemini 2.5 Flash Lite",
            ModelTier.Fast, SupportsTools: true, ContextWindow: 1_000_000,
            CostInputPerMillion: 0.10m, CostOutputPerMillion: 0.40m),
        new ModelInfo(AiProvider.Gemini, "gemini-2.5-flash", "Gemini 2.5 Flash",
            ModelTier.Balanced, SupportsTools: true, ContextWindow: 1_000_000,
            CostInputPerMillion: 0.30m, CostOutputPerMillion: 1.20m),
    };
}
