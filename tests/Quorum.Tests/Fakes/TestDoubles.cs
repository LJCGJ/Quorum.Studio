using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Agents;

namespace Quorum.Tests.Fakes;

/// <summary>
/// Provider falso que devolve respostas pre-roteirizadas, uma por chamada. Deixa
/// o teste dirigir o loop passo a passo — sem rede, sem chave, sem custo.
/// Tambem registra quantas vezes foi chamado e o ultimo request recebido, para
/// os testes verificarem o que o loop enviou (ex.: se o system prompt chegou).
/// </summary>
public sealed class ScriptedProvider : IAiProvider
{
    private readonly Queue<CompletionResponse> _roteiro;

    public ScriptedProvider(params CompletionResponse[] respostas) =>
        _roteiro = new Queue<CompletionResponse>(respostas);

    public AiProvider Provider => AiProvider.Claude;

    public int CallCount { get; private set; }
    public CompletionRequest? LastRequest { get; private set; }

    public Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;
        var resposta = _roteiro.Count > 0
            ? _roteiro.Dequeue()
            : new CompletionResponse("(fim do roteiro)", Array.Empty<ToolCall>());
        return Task.FromResult(resposta);
    }

    public void Dispose() { /* fake: nada a liberar */ }
}

/// <summary>
/// Provider que sempre lança na chamada — simula chave invalida / sem rede /
/// cota estourada, para testar o encerramento com AgentStopReason.Failed.
/// </summary>
public sealed class ThrowingProvider : IAiProvider
{
    private readonly Exception _erro;

    // Default: uma falha OPERACIONAL realista (cota/rede chegam como excecao HTTP),
    // que a classificacao trata como recuperavel por fallback. Para simular um BUG
    // (que deve subir), passe explicitamente ex.: new NullReferenceException().
    public ThrowingProvider(Exception? erro = null) =>
        _erro = erro ?? new HttpRequestException("429 - cota estourada");

    public Quorum.Core.Models.AiProvider Provider => Quorum.Core.Models.AiProvider.Gemini;

    public Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken cancellationToken = default) =>
        throw _erro;

    public void Dispose() { }
}

/// <summary>
/// Simula um TIMEOUT DE REDE: o HttpClient lanca TaskCanceledException (que herda
/// de OperationCanceledException) SEM que o token do usuario esteja cancelado.
/// Usado para provar que o loop distingue timeout (→ Failed → fallback) de
/// cancelamento real do usuario (→ Cancelled).
/// </summary>
public sealed class TimeoutProvider : IAiProvider
{
    public Quorum.Core.Models.AiProvider Provider => Quorum.Core.Models.AiProvider.Claude;

    public Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken cancellationToken = default) =>
        // TaskCanceledException sem passar o token = timeout, nao cancelamento.
        throw new TaskCanceledException("The request was canceled due to timeout.");

    public void Dispose() { }
}
public sealed class UsageProvider : IAiProvider
{
    private readonly long _tokens;

    public UsageProvider(long tokens) => _tokens = tokens;

    public Quorum.Core.Models.AiProvider Provider => Quorum.Core.Models.AiProvider.Claude;

    public Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CompletionResponse(
            "concluido",
            Array.Empty<ToolCall>(),
            new TokenUsage(_tokens / 2, _tokens / 2)));

    public void Dispose() { }
}
public sealed class SpyToolExecutor : IToolExecutor
{
    private readonly string _resposta;
    private readonly bool _lanca;

    public SpyToolExecutor(string resposta = "ok", bool lanca = false)
    {
        _resposta = resposta;
        _lanca = lanca;
    }

    public List<string> ChamadasRecebidas { get; } = new();

    public IReadOnlyList<ToolDefinition> Tools { get; } = new[]
    {
        new ToolDefinition("navegar", "Navega ate uma URL",
            "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}}}")
    };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ChamadasRecebidas.Add(call.Name);
        if (_lanca)
            throw new HttpRequestException("falha operacional simulada da ferramenta");
        return Task.FromResult(new ToolResult(call.Id, _resposta));
    }
}

/// <summary>
/// Executor que lanca uma excecao configuravel na execucao — para testar que o
/// loop distingue falha OPERACIONAL da ferramenta (vira ToolResult de erro) de
/// BUG/fatal (sobe).
/// </summary>
public sealed class ThrowingToolExecutor : IToolExecutor
{
    private readonly Exception _erro;

    public ThrowingToolExecutor(Exception erro) => _erro = erro;

    public IReadOnlyList<ToolDefinition> Tools { get; } = new[]
    {
        new ToolDefinition("navegar", "Navega ate uma URL",
            "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}}}")
    };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default) =>
        throw _erro;
}
public sealed class CancelingToolExecutor : IToolExecutor
{
    private readonly CancellationTokenSource _cts;

    public CancelingToolExecutor(CancellationTokenSource cts) => _cts = cts;

    public IReadOnlyList<ToolDefinition> Tools { get; } = new[]
    {
        new ToolDefinition("navegar", "Navega ate uma URL",
            "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}}}")
    };

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        _cts.Cancel();
        cancellationToken.ThrowIfCancellationRequested(); // simula ferramenta abortada
        return Task.FromResult(new ToolResult(call.Id, "nao chega aqui"));
    }
}
