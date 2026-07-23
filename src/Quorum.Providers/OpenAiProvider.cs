using Microsoft.Extensions.AI;
using OpenAI;
using Quorum.Core.Ai;
using CoreProvider = Quorum.Core.Models.AiProvider;

namespace Quorum.Providers;

/// <summary>
/// Provider da OpenAI, sobre o SDK OFICIAL (pacote "OpenAI"), exposto como
/// <see cref="IChatClient"/> pelo Microsoft.Extensions.AI.OpenAI. Mesmo padrao do
/// <see cref="ClaudeProvider"/>: so constroi o cliente e delega a traducao ao
/// <see cref="ChatClientAiProvider"/>.
/// </summary>
public static class OpenAiProvider
{
    /// <summary>Cria um <see cref="IAiProvider"/> para a OpenAI.</summary>
    /// <param name="apiKey">Chave de API (formato sk-...).</param>
    /// <param name="modelId">Modelo a usar (ex.: gpt-4o-mini).</param>
    public static IAiProvider Create(string apiKey, string modelId)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Chave de API da OpenAI vazia.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Modelo da OpenAI nao informado.", nameof(modelId));

        // O ChatClient do SDK e vinculado a um modelo na construcao.
        var chat = new OpenAIClient(apiKey).GetChatClient(modelId);
        IChatClient chatClient = chat.AsIChatClient();

        return new ChatClientAiProvider(CoreProvider.OpenAI, chatClient, modelId);
    }
}
