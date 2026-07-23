using Microsoft.Extensions.AI;
using Quorum.Core.Ai;
using Quorum.Providers;
using Xunit;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using MeaiChatRole = Microsoft.Extensions.AI.ChatRole;
using CoreProvider = Quorum.Core.Models.AiProvider;
using ChatMessage = Quorum.Core.Ai.ChatMessage;
using ChatRole = Quorum.Core.Ai.ChatRole;

namespace Quorum.Tests;

/// <summary>
/// Testa o adaptador (Opcao B) sem chave nem rede: um IChatClient falso permite
/// verificar a traducao nos dois sentidos — nossos tipos -> MEAI e MEAI -> nossos.
/// </summary>
public class ChatClientAiProviderTests
{
    /// <summary>IChatClient falso: devolve uma resposta fixa e guarda o que recebeu.</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly ChatResponse _resposta;
        public IEnumerable<MeaiChatMessage>? UltimasMensagens { get; private set; }
        public ChatOptions? UltimasOpcoes { get; private set; }

        public FakeChatClient(ChatResponse resposta) => _resposta = resposta;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<MeaiChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            UltimasMensagens = messages.ToList();
            UltimasOpcoes = options;
            return Task.FromResult(_resposta);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<MeaiChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public bool FoiDescartado { get; private set; }
        public void Dispose() => FoiDescartado = true;
    }

    private static ChatClientAiProvider Wrap(ChatResponse resposta, out FakeChatClient fake)
    {
        fake = new FakeChatClient(resposta);
        return new ChatClientAiProvider(CoreProvider.Claude, fake);
    }

    [Fact]
    public async Task Traduz_resposta_de_texto()
    {
        var resposta = new ChatResponse(
            new MeaiChatMessage(MeaiChatRole.Assistant, "Ola do Claude"));
        var provider = Wrap(resposta, out _);

        var r = await provider.CompleteAsync(new CompletionRequest(
            "claude-x", new[] { ChatMessage.FromUser("oi") }));

        Assert.Equal("Ola do Claude", r.Text);
        Assert.False(r.HasToolCalls);
    }

    [Fact]
    public async Task Traduz_tool_call_da_ia()
    {
        var msg = new MeaiChatMessage(MeaiChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call-1", "navegar",
                new Dictionary<string, object?> { ["url"] = "https://x.com" })
        });
        var provider = Wrap(new ChatResponse(msg), out _);

        var r = await provider.CompleteAsync(new CompletionRequest(
            "claude-x", new[] { ChatMessage.FromUser("navegue") }));

        Assert.True(r.HasToolCalls);
        Assert.Equal("navegar", r.ToolCalls[0].Name);
        Assert.Equal("call-1", r.ToolCalls[0].Id);
        Assert.Contains("x.com", r.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task System_prompt_vira_mensagem_system_para_o_IChatClient()
    {
        var provider = Wrap(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")),
            out var fake);

        await provider.CompleteAsync(new CompletionRequest(
            "claude-x", new[] { ChatMessage.FromUser("oi") },
            SystemPrompt: "voce e o Quorum"));

        // O system prompt precisa chegar como primeira mensagem, papel System.
        var primeira = fake.UltimasMensagens!.First();
        Assert.Equal(MeaiChatRole.System, primeira.Role);
        Assert.Contains("Quorum", primeira.Text);
    }

    [Fact]
    public async Task Ferramentas_declaradas_viram_AITools_nas_opcoes()
    {
        var provider = Wrap(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")),
            out var fake);

        var tools = new[]
        {
            new ToolDefinition("consultar", "Consulta algo",
                "{\"type\":\"object\",\"properties\":{}}")
        };
        await provider.CompleteAsync(new CompletionRequest(
            "claude-x", new[] { ChatMessage.FromUser("oi") }, Tools: tools));

        Assert.NotNull(fake.UltimasOpcoes!.Tools);
        Assert.Single(fake.UltimasOpcoes.Tools!);
        Assert.Equal("consultar", fake.UltimasOpcoes.Tools![0].Name);
    }

    [Fact]
    public async Task Model_id_e_repassado_nas_opcoes()
    {
        var provider = Wrap(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")),
            out var fake);

        await provider.CompleteAsync(new CompletionRequest(
            "claude-haiku-4-5-20251001", new[] { ChatMessage.FromUser("oi") }));

        Assert.Equal("claude-haiku-4-5-20251001", fake.UltimasOpcoes!.ModelId);
    }

    [Fact]
    public async Task Resultado_de_ferramenta_com_erro_e_sinalizado_a_ia()
    {
        var provider = Wrap(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")),
            out var fake);

        // Mensagem de papel Tool com um resultado marcado como erro.
        var msgTool = new ChatMessage(ChatRole.Tool, ToolResults: new[]
        {
            new ToolResult("call-1", "deu ruim", IsError: true)
        });
        await provider.CompleteAsync(new CompletionRequest(
            "m", new[] { ChatMessage.FromUser("x"), msgTool }));

        // Na traducao, o resultado de erro deve virar um FunctionResultContent
        // com Exception preenchida — e assim a IA sabe que a ferramenta falhou.
        var frc = fake.UltimasMensagens!
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .First(c => c.CallId == "call-1");
        Assert.NotNull(frc.Exception);
    }

    [Fact]
    public void Dispose_repassa_ao_ichatclient()
    {
        var fake = new FakeChatClient(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")));
        var provider = new ChatClientAiProvider(CoreProvider.Claude, fake);

        provider.Dispose();

        Assert.True(fake.FoiDescartado);
    }

    [Fact]
    public async Task Modelo_diferente_do_vinculado_falha_alto()
    {
        // Provider vinculado a "modelo-a"; requisicao pede "modelo-b". E descompasso
        // de programacao: deve lancar, nao depender de o SDK honrar o override.
        var fake = new FakeChatClient(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")));
        using var provider = new ChatClientAiProvider(CoreProvider.Claude, fake, "modelo-a");

        await Assert.ThrowsAsync<ArgumentException>(() => provider.CompleteAsync(
            new CompletionRequest("modelo-b", new[] { ChatMessage.FromUser("oi") })));
    }

    [Fact]
    public async Task Modelo_igual_ao_vinculado_passa_normalmente()
    {
        var fake = new FakeChatClient(
            new ChatResponse(new MeaiChatMessage(MeaiChatRole.Assistant, "ok")));
        using var provider = new ChatClientAiProvider(CoreProvider.Claude, fake, "modelo-a");

        var r = await provider.CompleteAsync(
            new CompletionRequest("modelo-a", new[] { ChatMessage.FromUser("oi") }));

        Assert.Equal("ok", r.Text);
    }
}
