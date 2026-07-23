using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Quorum.Core.Ai;
using CoreProvider = Quorum.Core.Models.AiProvider;
using ChatMessageMEAI = Microsoft.Extensions.AI.ChatMessage;
using ChatRoleMEAI = Microsoft.Extensions.AI.ChatRole;
using QuorumChatMessage = Quorum.Core.Ai.ChatMessage;
using QuorumChatRole = Quorum.Core.Ai.ChatRole;

namespace Quorum.Providers;

/// <summary>
/// Adaptador que liga QUALQUER <see cref="IChatClient"/> (a abstracao do
/// Microsoft.Extensions.AI) ao nosso <see cref="IAiProvider"/> neutro.
///
/// Esta e a peca central da "Opcao B": Claude, OpenAI e Gemini expoem, cada um,
/// um IChatClient — entao a traducao de/para os nossos tipos (ChatMessage,
/// ToolCall, ToolDefinition) fica AQUI, uma unica vez, em vez de tres. E o
/// antidoto direto para a duplicacao que gerou bugs na v4.2 (ex.: Gemini sem
/// system prompt): existe um so caminho de montagem da chamada.
/// </summary>
public sealed class ChatClientAiProvider : IAiProvider, IDisposable
{
    private readonly IChatClient _client;
    private readonly string? _boundModelId;

    /// <param name="provider">Provedor que esta instancia atende.</param>
    /// <param name="client">Cliente ja construido pelo provider concreto.</param>
    /// <param name="boundModelId">
    /// Modelo ao qual o <paramref name="client"/> foi vinculado na construcao (os
    /// tres SDKs pedem um). Quando informado, cada requisicao e validada contra
    /// ele: pedir outro modelo lanca em vez de depender do SDK honrar — ou nao —
    /// o override em ChatOptions.ModelId. Nulo desativa a checagem (util em testes
    /// com um IChatClient falso que nao esta preso a modelo nenhum).
    /// </param>
    public ChatClientAiProvider(CoreProvider provider, IChatClient client, string? boundModelId = null)
    {
        Provider = provider;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _boundModelId = boundModelId;
    }

    public CoreProvider Provider { get; }

    /// <summary>Libera o IChatClient subjacente (e o HttpClient que ele mantem).</summary>
    public void Dispose() => _client.Dispose();

    public async Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken cancellationToken = default)
    {
        // O cliente e criado JA vinculado a um modelo (exigencia dos tres SDKs), e
        // a fabrica cria um provider por modelo. Se a requisicao pedir outro, e um
        // descompasso de programacao: falha alto aqui, em vez de deixar o resultado
        // depender de o SDK honrar ou ignorar o override em ChatOptions.ModelId.
        // ArgumentException e classificada como bug (AiFailureClassifier), entao
        // sobe em vez de virar fallback silencioso.
        if (_boundModelId is not null &&
            !string.Equals(_boundModelId, request.ModelId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Este provider esta vinculado ao modelo '{_boundModelId}', mas a " +
                $"requisicao pediu '{request.ModelId}'. Crie um provider para o " +
                "modelo desejado (AiProviderFactory.Create).", nameof(request));
        }

        var messages = BuildMessages(request);
        var options = BuildOptions(request);

        var response = await _client.GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        return TranslateResponse(response);
    }

    // ---- nosso mundo -> Microsoft.Extensions.AI -------------------------------

    private static List<ChatMessageMEAI> BuildMessages(CompletionRequest request)
    {
        var list = new List<ChatMessageMEAI>();

        // System prompt entra como mensagem de papel System. O IChatClient de cada
        // provedor cuida de coloca-lo no lugar certo do formato nativo — e por isso
        // que nenhum provedor "esquece" o system prompt nesta arquitetura.
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            list.Add(new ChatMessageMEAI(ChatRoleMEAI.System, request.SystemPrompt));

        foreach (var m in request.Messages)
            list.Add(TranslateMessage(m));

        return list;
    }

    private static ChatMessageMEAI TranslateMessage(QuorumChatMessage m)
    {
        var role = m.Role switch
        {
            QuorumChatRole.System => ChatRoleMEAI.System,
            QuorumChatRole.User => ChatRoleMEAI.User,
            QuorumChatRole.Assistant => ChatRoleMEAI.Assistant,
            QuorumChatRole.Tool => ChatRoleMEAI.Tool,
            _ => ChatRoleMEAI.User
        };

        var contents = new List<AIContent>();

        if (!string.IsNullOrEmpty(m.Text))
            contents.Add(new TextContent(m.Text));

        // Pedidos de ferramenta feitos pelo assistente
        if (m.ToolCalls is not null)
        {
            foreach (var call in m.ToolCalls)
            {
                var args = ParseArgs(call.ArgumentsJson);
                contents.Add(new FunctionCallContent(call.Id, call.Name, args));
            }
        }

        // Resultados de ferramenta devolvidos a IA
        if (m.ToolResults is not null)
        {
            foreach (var res in m.ToolResults)
            {
                var frc = new FunctionResultContent(res.ToolCallId, res.Content);
                // Sinaliza a falha para a IA poder reagir (nao so ver o texto do erro).
                if (res.IsError)
                    frc.Exception = new InvalidOperationException(res.Content);
                contents.Add(frc);
            }
        }

        return new ChatMessageMEAI(role, contents);
    }

    private static ChatOptions BuildOptions(CompletionRequest request)
    {
        var options = new ChatOptions
        {
            // Redundante com o modelo vinculado ao cliente — mantido de proposito:
            // e o campo padrao do Microsoft.Extensions.AI, aparece em telemetria e
            // logs dos SDKs, e serve para clientes que NAO fixam modelo na
            // construcao. A validacao em CompleteAsync garante que os dois valores
            // sempre coincidem, entao nao ha comportamento dependente de SDK.
            ModelId = request.ModelId,
            MaxOutputTokens = request.MaxTokens
        };

        if (request.Tools is { Count: > 0 })
        {
            options.Tools = request.Tools
                .Select(t => (AITool)new QuorumDeclaredFunction(t))
                .ToList();
        }

        return options;
    }

    // ---- Microsoft.Extensions.AI -> nosso mundo -------------------------------

    private static CompletionResponse TranslateResponse(ChatResponse response)
    {
        var text = new StringBuilder();
        var toolCalls = new List<ToolCall>();

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent tc:
                        text.Append(tc.Text);
                        break;
                    case FunctionCallContent fc:
                        toolCalls.Add(new ToolCall(
                            fc.CallId,
                            fc.Name,
                            SerializeArgs(fc.Arguments)));
                        break;
                }
            }
        }

        // Uso de tokens (custo real da chamada), quando o provedor informa.
        var usage = response.Usage is { } u
            ? new TokenUsage(u.InputTokenCount, u.OutputTokenCount)
            : TokenUsage.Unknown;

        // Truncamento: o modelo parou por atingir o teto de tokens, entao o texto
        // pode estar incompleto. So marcamos quando NAO houve tool calls (nesse
        // caso "Length" e esperado, pois a resposta continua no proximo passo).
        var truncated = response.FinishReason == ChatFinishReason.Length && toolCalls.Count == 0;

        return new CompletionResponse(text.ToString().Trim(), toolCalls, usage, truncated);
    }

    // ---- helpers de (de)serializacao de argumentos ----------------------------

    private static IDictionary<string, object?> ParseArgs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            return dict ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static string SerializeArgs(IEnumerable<KeyValuePair<string, object?>>? args)
    {
        if (args is null) return "{}";
        var dict = args.ToDictionary(kv => kv.Key, kv => kv.Value);
        return JsonSerializer.Serialize(dict);
    }
}
