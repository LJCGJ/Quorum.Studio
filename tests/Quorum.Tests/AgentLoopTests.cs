using Quorum.Core.Ai;
using Quorum.Agents;
using Quorum.Tests.Fakes;
using Xunit;

namespace Quorum.Tests;

public class AgentLoopTests
{
    private static ToolCall UmaChamada(string id = "c1") =>
        new(id, "navegar", "{\"url\":\"https://exemplo.com\"}");

    [Fact]
    public async Task Sem_tool_calls_conclui_no_primeiro_passo()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("Relatorio pronto.", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste o login", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal("Relatorio pronto.", r.FinalText);
        Assert.Equal(1, r.StepsUsed);
    }

    [Fact]
    public async Task Executa_ferramenta_e_depois_conclui()
    {
        // Passo 1: IA pede a ferramenta. Passo 2: IA conclui com texto.
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("Login testado com sucesso.", Array.Empty<ToolCall>()));
        var executor = new SpyToolExecutor("pagina carregada");
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste o login", executor);

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal("Login testado com sucesso.", r.FinalText);
        Assert.Equal(2, r.StepsUsed);
        Assert.Single(executor.ChamadasRecebidas);
        Assert.Equal("navegar", executor.ChamadasRecebidas[0]);
    }

    [Fact]
    public async Task Atinge_limite_de_passos_quando_ia_nunca_conclui()
    {
        // IA pede ferramenta em TODA resposta: o loop nunca "conclui" sozinho.
        var respostas = Enumerable.Range(0, 10)
            .Select(i => new CompletionResponse("", new[] { UmaChamada($"c{i}") }))
            .ToArray();
        var provider = new ScriptedProvider(respostas);
        var loop = new AgentLoop(provider, new AgentLoopOptions(MaxSteps: 3));

        var r = await loop.RunAsync("m", "sys", "loop infinito", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.StepLimitReached, r.StopReason);
        Assert.Equal(3, r.StepsUsed);
        Assert.Contains("Limite de passos", r.FinalText);
    }

    [Fact]
    public async Task Ferramenta_que_lanca_nao_derruba_o_loop()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("Segui apesar do erro.", Array.Empty<ToolCall>()));
        var executor = new SpyToolExecutor(lanca: true); // ExecuteAsync lanca
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", executor);

        // O loop capturou a excecao, devolveu o erro a IA e ela concluiu.
        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal("Segui apesar do erro.", r.FinalText);
    }

    [Fact]
    public async Task Falha_operacional_da_ferramenta_vira_erro_para_a_ia()
    {
        // HttpRequestException do executor (ex.: HTTP 500) e operacional: a IA
        // recebe o erro como resultado e pode reagir; o loop nao morre.
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("Reportei a falha.", Array.Empty<ToolCall>()));
        var executor = new ThrowingToolExecutor(new HttpRequestException("500"));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", executor);

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        // O resultado de erro chegou marcado a IA no request seguinte.
        var msgTool = provider.LastRequest!.Messages.First(m => m.Role == ChatRole.Tool);
        Assert.True(msgTool.ToolResults![0].IsError);
    }

    [Fact]
    public async Task Bug_no_executor_sobe_e_nao_vira_texto_para_a_ia()
    {
        // NullReference dentro de um agente concreto e bug NOSSO: deve subir para
        // aparecer, nao ser convertido em ToolResult de erro e escondido.
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("nao chega aqui", Array.Empty<ToolCall>()));
        var executor = new ThrowingToolExecutor(new NullReferenceException("bug no agente"));
        var loop = new AgentLoop(provider);

        await Assert.ThrowsAsync<NullReferenceException>(
            () => loop.RunAsync("m", "sys", "teste", executor));
    }

    [Fact]
    public async Task System_prompt_chega_ao_provider()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("ok", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider);

        await loop.RunAsync("m", "voce e o Quorum", "oi", new SpyToolExecutor());

        // Garante que o system prompt nao se perde (o bug do Gemini na v4.2).
        Assert.Equal("voce e o Quorum", provider.LastRequest!.SystemPrompt);
    }

    [Fact]
    public async Task Resultado_de_ferramenta_e_truncado_ao_limite()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("fim", Array.Empty<ToolCall>()));
        var executor = new SpyToolExecutor(new string('x', 5000));
        var loop = new AgentLoop(provider, new AgentLoopOptions(MaxToolResultChars: 100));

        await loop.RunAsync("m", "sys", "teste", executor);

        // O segundo request enviado a IA carrega o resultado ja truncado: o trecho
        // de 'x' vai ate no maximo 100 chars (bem menos que os 5000 originais),
        // seguido do marcador de truncamento.
        var msgTool = provider.LastRequest!.Messages.First(m => m.Role == ChatRole.Tool);
        var conteudo = msgTool.ToolResults![0].Content;
        var xs = conteudo.TakeWhile(c => c == 'x').Count();
        Assert.True(xs <= 100);
        Assert.Contains("[resultado truncado]", conteudo);
    }

    [Fact]
    public async Task Cancelamento_encerra_com_motivo_cancelled()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("nunca chega aqui", Array.Empty<ToolCall>()));
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // ja cancelado antes de comecar
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor(), cts.Token);

        Assert.Equal(AgentStopReason.Cancelled, r.StopReason);
    }

    [Fact]
    public async Task Callback_de_progresso_reporta_ferramentas()
    {
        var progresso = new List<string>();
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("fim", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider, onProgress: progresso.Add);

        await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor());

        Assert.Contains(progresso, p => p.Contains("navegar"));
    }

    [Fact]
    public async Task Cancelamento_no_meio_da_execucao_retorna_cancelled_sem_lancar()
    {
        // IA pede ferramenta; o executor cancela o token durante a execucao.
        // Deve encerrar com Cancelled (nao propagar OperationCanceledException).
        using var cts = new CancellationTokenSource();
        var provider = new ScriptedProvider(
            new CompletionResponse("", new[] { UmaChamada() }),
            new CompletionResponse("nao deveria chegar", Array.Empty<ToolCall>()));
        var executor = new CancelingToolExecutor(cts);
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", executor, cts.Token);

        Assert.Equal(AgentStopReason.Cancelled, r.StopReason);
    }

    [Fact]
    public async Task Falha_operacional_da_ia_encerra_com_failed_e_nao_propaga()
    {
        // Falha operacional realista (cota/rede vem como HttpRequestException):
        // deve encerrar com Failed, para o fallback poder tentar outro modelo.
        var provider = new ThrowingProvider(new HttpRequestException("429 - sem cota"));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor());

        Assert.Equal(AgentStopReason.Failed, r.StopReason);
        Assert.Contains("sem cota", r.FinalText);
    }

    [Fact]
    public async Task Bug_de_programacao_sobe_e_nao_e_mascarado_como_failed()
    {
        // Um NullReference e bug NOSSO: nao pode virar "Failed" (que dispararia
        // fallback e mascararia o defeito). Deve propagar para aparecer.
        var provider = new ThrowingProvider(new NullReferenceException("bug interno"));
        var loop = new AgentLoop(provider);

        await Assert.ThrowsAsync<NullReferenceException>(
            () => loop.RunAsync("m", "sys", "teste", new SpyToolExecutor()));
    }

    [Fact]
    public async Task Timeout_de_rede_vira_failed_e_nao_cancelled()
    {
        // TaskCanceledException SEM o token cancelado = timeout do provedor.
        // Deve virar Failed (para o fallback tentar outro modelo), nao Cancelled.
        var loop = new AgentLoop(new TimeoutProvider());

        // Token NAO cancelado, de proposito.
        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor(), CancellationToken.None);

        Assert.Equal(AgentStopReason.Failed, r.StopReason);
    }

    [Fact]
    public async Task Cancelamento_real_do_usuario_continua_sendo_cancelled()
    {
        // Mesmo provider de timeout, mas agora o token ESTA cancelado: e o usuario.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var loop = new AgentLoop(new TimeoutProvider());

        var r = await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor(), cts.Token);

        Assert.Equal(AgentStopReason.Cancelled, r.StopReason);
    }

    [Fact]
    public async Task NotSupportedException_sobe_e_nao_vira_failed()
    {
        // QuorumDeclaredFunction invocada indevidamente lanca NotSupported: e erro
        // de configuracao que deve aparecer, nao ser mascarado pelo fallback.
        var provider = new ThrowingProvider(new NotSupportedException("uso indevido"));
        var loop = new AgentLoop(provider);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => loop.RunAsync("m", "sys", "teste", new SpyToolExecutor()));
    }

    [Fact]
    public async Task Objetivo_vazio_e_rejeitado()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("ok", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider);

        await Assert.ThrowsAsync<ArgumentException>(
            () => loop.RunAsync("m", "sys", "   ", new SpyToolExecutor()));
    }

    [Fact]
    public async Task MaxTokens_das_opcoes_chega_ao_provider()
    {
        var provider = new ScriptedProvider(
            new CompletionResponse("ok", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider, new AgentLoopOptions(MaxTokens: 999));

        await loop.RunAsync("m", "sys", "teste", new SpyToolExecutor());

        Assert.Equal(999, provider.LastRequest!.MaxTokens);
    }
}
