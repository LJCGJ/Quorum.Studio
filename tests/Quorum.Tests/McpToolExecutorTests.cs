using Quorum.Agents;
using Quorum.Core.Ai;
using Quorum.Mcp;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Testa a ponte MCP -> loop agentic SEM Node instalado: a sessao falsa substitui
/// o servidor externo. E o que permite estes testes rodarem no CI dos tres SOs.
/// </summary>
public class McpToolExecutorTests
{
    /// <summary>Sessao falsa: ferramentas fixas e resposta (ou falha) programada.</summary>
    private sealed class FakeSession : IMcpSession
    {
        private readonly string _resposta;
        private readonly Exception? _erro;
        private readonly bool _protocoloErro;

        public FakeSession(string resposta = "ok", Exception? erro = null, bool protocoloErro = false)
        {
            _resposta = resposta;
            _erro = erro;
            _protocoloErro = protocoloErro;
        }

        public List<(string Nome, string Args)> Chamadas { get; } = new();
        public bool FoiDescartada { get; private set; }

        public Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken ct = default)
        {
            IReadOnlyList<ToolDefinition> tools = new[]
            {
                new ToolDefinition("browser_navigate", "Navega ate uma URL",
                    "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}}}"),
                new ToolDefinition("browser_snapshot", "Captura a estrutura da pagina",
                    "{\"type\":\"object\",\"properties\":{}}")
            };
            return Task.FromResult(tools);
        }

        public Task<McpToolResponse> CallToolAsync(string name, string argumentsJson, CancellationToken ct = default)
        {
            Chamadas.Add((name, argumentsJson));
            if (_erro is not null) throw _erro;
            return Task.FromResult(new McpToolResponse(_resposta, _protocoloErro));
        }

        public ValueTask DisposeAsync()
        {
            FoiDescartada = true;
            return ValueTask.CompletedTask;
        }
    }

    private static ToolCall Chamada(string nome = "browser_navigate") =>
        new("c1", nome, "{\"url\":\"https://exemplo.com\"}");

    [Fact]
    public async Task Expoe_as_ferramentas_do_servidor()
    {
        var executor = await McpToolExecutor.CreateAsync(new FakeSession());

        Assert.Equal(2, executor.Tools.Count);
        Assert.Contains(executor.Tools, t => t.Name == "browser_navigate");
    }

    [Fact]
    public async Task Encaminha_a_chamada_ao_servidor_e_devolve_o_resultado()
    {
        var sessao = new FakeSession("pagina carregada");
        var executor = await McpToolExecutor.CreateAsync(sessao);

        var r = await executor.ExecuteAsync(Chamada());

        Assert.False(r.IsError);
        Assert.Equal("pagina carregada", r.Content);
        Assert.Single(sessao.Chamadas);
        Assert.Equal("browser_navigate", sessao.Chamadas[0].Nome);
    }

    [Fact]
    public async Task Falha_operacional_do_servidor_vira_erro_para_a_ia()
    {
        // InvalidOperationException e o caso classico: o classificador GENERICO a
        // trataria como bug, mas aqui ela e operacional (sessao/ferramenta) e deve
        // virar resultado de erro para a IA reagir.
        var sessao = new FakeSession(erro: new InvalidOperationException("sessao encerrada"));
        var executor = await McpToolExecutor.CreateAsync(sessao);

        var r = await executor.ExecuteAsync(Chamada());

        Assert.True(r.IsError);
        Assert.Contains("sessao encerrada", r.Content);
    }

    [Fact]
    public async Task Bug_nosso_sobe_e_nao_vira_texto_para_a_ia()
    {
        var sessao = new FakeSession(erro: new NullReferenceException("bug interno"));
        var executor = await McpToolExecutor.CreateAsync(sessao);

        await Assert.ThrowsAsync<NullReferenceException>(
            () => executor.ExecuteAsync(Chamada()));
    }

    [Fact]
    public async Task Cancelamento_do_usuario_sobe_para_o_loop_encerrar()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sessao = new FakeSession(erro: new OperationCanceledException());
        var executor = await McpToolExecutor.CreateAsync(sessao);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(Chamada(), cts.Token));
    }

    [Fact]
    public async Task Loop_agentic_usa_as_ferramentas_do_mcp_de_ponta_a_ponta()
    {
        // Prova a integracao completa sem Node e sem IA real: provider roteirizado
        // pede a ferramenta, o executor MCP responde, a IA conclui.
        var sessao = new FakeSession("titulo: Exemplo");
        var executor = await McpToolExecutor.CreateAsync(sessao);
        var provider = new Fakes.ScriptedProvider(
            new CompletionResponse("", new[] { Chamada() }),
            new CompletionResponse("Naveguei e li o titulo.", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", "sys", "abra o site", executor);

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Equal("Naveguei e li o titulo.", r.FinalText);
        Assert.Single(sessao.Chamadas);
    }

    [Fact]
    public async Task Erro_sinalizado_pelo_protocolo_marca_o_resultado()
    {
        // O servidor respondeu com isError=true (sem lancar excecao). O ToolResult
        // precisa sair marcado, para o adaptador preencher a excecao no
        // FunctionResultContent e a IA saber que a ferramenta falhou.
        var sessao = new FakeSession("elemento nao encontrado", protocoloErro: true);
        var executor = await McpToolExecutor.CreateAsync(sessao);

        var r = await executor.ExecuteAsync(Chamada());

        Assert.True(r.IsError);
        Assert.Equal("elemento nao encontrado", r.Content);
    }

    [Fact]
    public async Task Sucesso_do_protocolo_nao_marca_erro()
    {
        var sessao = new FakeSession("tudo certo", protocoloErro: false);
        var executor = await McpToolExecutor.CreateAsync(sessao);

        var r = await executor.ExecuteAsync(Chamada());

        Assert.False(r.IsError);
    }
}
