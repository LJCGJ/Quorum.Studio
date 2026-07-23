using Google.GenAI;
using Microsoft.Extensions.AI;
using Quorum.Core.Ai;
using CoreProvider = Quorum.Core.Models.AiProvider;

namespace Quorum.Providers;

/// <summary>
/// Provider do Gemini, sobre o SDK OFICIAL do Google (pacote "Google.GenAI", do
/// repositorio googleapis/dotnet-genai), que expoe <see cref="IChatClient"/> via
/// a extensao AsIChatClient. Mesmo padrao dos outros dois providers.
///
/// Nota sobre chaves: o Google mudou o formato (AIza... -> AQ....) e pode mudar
/// de novo — por isso a deteccao de provedor no <c>ModelRegistry</c> trata o
/// Gemini como PADRAO, em vez de validar um prefixo especifico.
/// </summary>
public static class GeminiProvider
{
    /// <summary>Cria um <see cref="IAiProvider"/> para o Gemini.</summary>
    /// <param name="apiKey">Chave de API (AIza..., AQ... ou formato futuro).</param>
    /// <param name="modelId">Modelo a usar (ex.: gemini-2.5-flash).</param>
    public static IAiProvider Create(string apiKey, string modelId)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Chave de API do Gemini vazia.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Modelo do Gemini nao informado.", nameof(modelId));

        var client = new Client(apiKey: apiKey);
        IChatClient chatClient = client.AsIChatClient(modelId);

        return new ChatClientAiProvider(CoreProvider.Gemini, chatClient, modelId);
    }
}
