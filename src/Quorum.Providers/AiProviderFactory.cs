using Quorum.Core.Ai;
using Quorum.Core.Routing;
using CoreProvider = Quorum.Core.Models.AiProvider;

namespace Quorum.Providers;

/// <summary>
/// Monta o <see cref="IAiProvider"/> correto a partir de uma chave de API,
/// detectando o provedor pelo prefixo (mesma heuristica do <see cref="ModelRegistry"/>).
/// E o ponto unico onde chave + modelo viram um provider pronto para uso.
///
/// O provider e criado POR MODELO porque os tres SDKs vinculam um modelo ao
/// cliente na construcao. Isso casa com o <c>FallbackAgentRunner</c>, cuja fabrica
/// (<c>Func&lt;string, IAiProvider&gt;</c>) ja recebe o Id do modelo da vez.
/// </summary>
public static class AiProviderFactory
{
    /// <summary>Cria o provider adequado para a chave informada.</summary>
    /// <param name="apiKey">Chave de API; o prefixo determina o provedor.</param>
    /// <param name="modelId">Modelo a usar nesta instancia.</param>
    public static IAiProvider Create(string apiKey, string modelId)
    {
        var provider = ModelRegistry.DetectProvider(apiKey);
        return provider switch
        {
            CoreProvider.Claude => ClaudeProvider.Create(apiKey, modelId),
            CoreProvider.OpenAI => OpenAiProvider.Create(apiKey, modelId),
            CoreProvider.Gemini => GeminiProvider.Create(apiKey, modelId),
            _ => throw new NotSupportedException($"Provedor nao suportado: {provider}")
        };
    }
}
