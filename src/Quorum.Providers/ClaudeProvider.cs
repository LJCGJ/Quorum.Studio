using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using Quorum.Core.Ai;
using CoreProvider = Quorum.Core.Models.AiProvider;

namespace Quorum.Providers;

/// <summary>
/// Provider do Claude, sobre o SDK OFICIAL da Anthropic (pacote "Anthropic" 12+).
/// Sua unica responsabilidade e construir um <see cref="IChatClient"/> e entregar
/// ao <see cref="ChatClientAiProvider"/>, que faz toda a traducao. Nenhuma logica
/// de conversa vive aqui — ela e comum aos tres provedores, no adaptador.
/// </summary>
public static class ClaudeProvider
{
    /// <summary>Cria um <see cref="IAiProvider"/> para o Claude.</summary>
    /// <param name="apiKey">Chave de API (formato sk-ant-...).</param>
    /// <param name="modelId">
    /// Modelo do cliente. Os SDKs dos tres provedores pedem um modelo na
    /// construcao; por isso o provider e criado POR MODELO — o que casa com o
    /// <c>FallbackAgentRunner</c>, que ja instancia um provider para cada modelo
    /// da cadeia de fallback.
    /// </param>
    public static IAiProvider Create(string apiKey, string modelId)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Chave de API do Claude vazia.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Modelo do Claude nao informado.", nameof(modelId));

        var client = new AnthropicClient(new ClientOptions { ApiKey = apiKey });
        IChatClient chatClient = client.AsIChatClient(modelId);

        return new ChatClientAiProvider(CoreProvider.Claude, chatClient, modelId);
    }
}
