using Quorum.Core.Ai;

namespace Quorum.Agents;

/// <summary>
/// O loop agentic generico: IA pede ferramentas → executor as executa → resultados
/// voltam para a IA → repete ate a IA concluir (responder sem pedir ferramenta) ou
/// atingir o teto de passos.
///
/// Este e o coracao reaproveitado da v5. Na v4.2 este loop estava TRIPLICADO em
/// Python (loop_anthropic, loop_openai, loop_gemini), o que gerou bugs como o
/// Gemini sem system prompt. Aqui ele e UNICO: fala com qualquer <see cref="IAiProvider"/>
/// e com qualquer <see cref="IToolExecutor"/>, entao serve igualmente para Tela,
/// API, Banco, Oracle e Mongo — muda so o executor injetado.
///
/// Reporta progresso via callback (equivalente aos logs ">>>" que a v4.2 mandava
/// para stderr e a UI mostrava ao vivo).
/// </summary>
public sealed class AgentLoop
{
    private readonly IAiProvider _provider;
    private readonly AgentLoopOptions _options;
    private readonly Action<string>? _onProgress;

    public AgentLoop(
        IAiProvider provider,
        AgentLoopOptions? options = null,
        Action<string>? onProgress = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? AgentLoopOptions.Default;
        _onProgress = onProgress;
    }

    /// <summary>
    /// Roda o loop ate concluir ou atingir o limite de passos.
    /// </summary>
    /// <param name="modelId">Modelo escolhido pelo roteador.</param>
    /// <param name="systemPrompt">Instrucao de sistema (persona + regras).</param>
    /// <param name="objective">Objetivo do usuario (primeira mensagem).</param>
    /// <param name="executor">Executor que liga as ferramentas ao mundo real.</param>
    /// <param name="ct">Cancelamento (usuario ou timeout).</param>
    public async Task<AgentResult> RunAsync(
        string modelId,
        string systemPrompt,
        string objective,
        IToolExecutor executor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (string.IsNullOrWhiteSpace(objective))
            throw new ArgumentException("O objetivo da tarefa nao pode ser vazio.", nameof(objective));

        var messages = new List<ChatMessage> { ChatMessage.FromUser(objective) };
        var lastText = string.Empty;
        long? totalTokens = null;

        for (var step = 1; step <= _options.MaxSteps; step++)
        {
            if (ct.IsCancellationRequested)
                return new AgentResult(FallbackText(lastText), AgentStopReason.Cancelled, step - 1, totalTokens);

            var request = new CompletionRequest(
                ModelId: modelId,
                Messages: messages,
                Tools: executor.Tools,
                SystemPrompt: systemPrompt,
                MaxTokens: _options.MaxTokens);

            CompletionResponse response;
            try
            {
                response = await _provider.CompleteAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento REAL do usuario (ou timeout externo via token).
                return new AgentResult(FallbackText(lastText), AgentStopReason.Cancelled, step - 1, totalTokens);
            }
            catch (Exception ex) when (AiFailureClassifier.IsRecoverable(ex))
            {
                // Falha OPERACIONAL da IA (chave invalida, sem rede, cota
                // estourada, TIMEOUT do provedor). Timeout de HttpClient chega como
                // TaskCanceledException (herda de OperationCanceledException), mas
                // SEM o token cancelado — por isso o filtro acima nao a captura e
                // ela cai aqui, virando Failed para o fallback poder tentar outro
                // modelo. Bugs e excecoes fatais NAO caem aqui (ver
                // AiFailureClassifier): sobem, para nao serem mascarados.
                Report($">>> Falha na chamada a IA: {ex.Message}");
                return new AgentResult(
                    $"A chamada a IA falhou: {ex.Message}",
                    AgentStopReason.Failed,
                    step - 1,
                    totalTokens);
            }

            // Acumula o uso de tokens deste passo (quando o provedor informa).
            if (response.Usage?.Total is { } passo)
                totalTokens = (totalTokens ?? 0) + passo;

            // Sem pedidos de ferramenta = resposta final: tarefa concluida.
            if (!response.HasToolCalls)
                return new AgentResult(
                    string.IsNullOrWhiteSpace(response.Text) ? FallbackText(lastText) : response.Text,
                    AgentStopReason.Completed,
                    step,
                    totalTokens,
                    OutputTruncated: response.Truncated);

            if (!string.IsNullOrWhiteSpace(response.Text))
                lastText = response.Text;

            // Registra a fala do assistente (texto + pedidos de ferramenta) no historico.
            messages.Add(new ChatMessage(
                ChatRole.Assistant, response.Text, ToolCalls: response.ToolCalls));

            // Executa cada ferramenta pedida e coleta os resultados.
            var results = new List<ToolResult>(response.ToolCalls.Count);
            foreach (var call in response.ToolCalls)
            {
                if (ct.IsCancellationRequested)
                    return new AgentResult(FallbackText(lastText), AgentStopReason.Cancelled, step, totalTokens);

                Report($">>> Ferramenta: {call.Name}");

                ToolResult result;
                try
                {
                    result = await executor.ExecuteAsync(call, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Cancelamento REAL do usuario: encerramento esperado.
                    return new AgentResult(FallbackText(lastText), AgentStopReason.Cancelled, step, totalTokens);
                }
                catch (Exception ex) when (AiFailureClassifier.IsRecoverable(ex))
                {
                    // Falha OPERACIONAL da ferramenta (query invalida, timeout do
                    // banco, HTTP 500, timeout interno via TaskCanceledException sem
                    // o token cancelado): nao derruba o loop. A IA recebe o erro e
                    // pode reagir (tentar outra abordagem, relatar). Bugs nossos e
                    // excecoes fatais NAO caem aqui (ver AiFailureClassifier): sobem,
                    // para aparecerem em vez de virar texto silencioso para a IA.
                    result = new ToolResult(call.Id, $"ERRO ao executar {call.Name}: {ex.Message}", IsError: true);
                }

                results.Add(Truncate(result));
            }

            // Devolve os resultados a IA como uma mensagem de papel Tool.
            messages.Add(new ChatMessage(ChatRole.Tool, ToolResults: results));
        }

        // Esgotou os passos sem a IA concluir.
        return new AgentResult(
            FallbackText(lastText) + "\n\n[Limite de passos atingido antes de concluir o objetivo.]",
            AgentStopReason.StepLimitReached,
            _options.MaxSteps,
            totalTokens);
    }

    private ToolResult Truncate(ToolResult r)
    {
        var max = _options.MaxToolResultChars;
        if (r.Content.Length <= max) return r;

        // Nao corta no meio de um par substituto UTF-16 (emoji, caracteres fora do
        // BMP): se o ultimo char mantido for um high surrogate, recua um.
        var corte = max;
        if (corte > 0 && char.IsHighSurrogate(r.Content[corte - 1]))
            corte--;

        // Sinaliza o corte para a IA nao concluir com base num resultado parcial
        // (ex.: achar que a query so retornou as linhas que ela viu).
        var conteudo = r.Content[..corte] + "\n[resultado truncado]";
        return r with { Content = conteudo };
    }

    private static string FallbackText(string lastText) =>
        string.IsNullOrWhiteSpace(lastText) ? "(sem resposta final)" : lastText;

    private void Report(string message) => _onProgress?.Invoke(message);
}
