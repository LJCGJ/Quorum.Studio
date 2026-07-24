using System.Net;
using System.Text;
using Quorum.Agents;
using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Mcp;
using Xunit;

namespace Quorum.Tests;

/// <summary>
/// Testes dos agentes concretos. O agente de API usa um handler HTTP falso (sem
/// rede) e os de MCP usam sessao falsa (sem Node) — tudo roda no CI, de graca.
/// </summary>
public class AgentsTests
{
    // ---------------------------------------------------------------- HTTP fake

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _corpo;
        private readonly Exception? _erro;

        public FakeHandler(HttpStatusCode status = HttpStatusCode.OK, string corpo = "{}", Exception? erro = null)
        {
            _status = status; _corpo = corpo; _erro = erro;
        }

        public HttpRequestMessage? UltimaRequisicao { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UltimaRequisicao = request;
            if (_erro is not null) throw _erro;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_corpo, Encoding.UTF8, "application/json")
            });
        }
    }

    private static ToolCall ChamadaHttp(string args) =>
        new("c1", HttpToolExecutor.ToolName, args);

    [Fact]
    public async Task Api_executa_requisicao_e_relata_status()
    {
        var handler = new FakeHandler(HttpStatusCode.Created, """{"id":7}""");
        using var executor = new HttpToolExecutor(new HttpClient(handler));

        var r = await executor.ExecuteAsync(ChamadaHttp(
            """{"metodo":"POST","url":"https://api.exemplo.com/itens","body":"{\"nome\":\"x\"}"}"""));

        Assert.False(r.IsError);
        Assert.Contains("201", r.Content);
        Assert.Contains("\"id\":7", r.Content);
        Assert.Equal(HttpMethod.Post, handler.UltimaRequisicao!.Method);
    }

    [Fact]
    public async Task Api_status_de_erro_nao_e_falha_da_ferramenta()
    {
        // 404 e resposta legitima: quem julga se era esperado e a IA.
        var handler = new FakeHandler(HttpStatusCode.NotFound, """{"erro":"nao encontrado"}""");
        using var executor = new HttpToolExecutor(new HttpClient(handler));

        var r = await executor.ExecuteAsync(ChamadaHttp(
            """{"metodo":"GET","url":"https://api.exemplo.com/x"}"""));

        Assert.False(r.IsError);
        Assert.Contains("404", r.Content);
    }

    [Fact]
    public async Task Api_falha_de_rede_vira_erro_para_a_ia()
    {
        var handler = new FakeHandler(erro: new HttpRequestException("DNS nao resolveu"));
        using var executor = new HttpToolExecutor(new HttpClient(handler));

        var r = await executor.ExecuteAsync(ChamadaHttp(
            """{"metodo":"GET","url":"https://inexistente.exemplo"}"""));

        Assert.True(r.IsError);
        Assert.Contains("DNS", r.Content);
    }

    [Fact]
    public async Task Api_url_invalida_vira_erro_para_o_modelo_corrigir()
    {
        using var executor = new HttpToolExecutor(new HttpClient(new FakeHandler()));

        var r = await executor.ExecuteAsync(ChamadaHttp(
            """{"metodo":"GET","url":"nao-e-url"}"""));

        Assert.True(r.IsError);
        Assert.Contains("URL invalida", r.Content);
    }

    [Fact]
    public async Task Api_headers_sao_enviados()
    {
        var handler = new FakeHandler();
        using var executor = new HttpToolExecutor(new HttpClient(handler));

        await executor.ExecuteAsync(ChamadaHttp(
            """{"metodo":"GET","url":"https://api.exemplo.com","headers":{"Authorization":"Bearer abc"}}"""));

        Assert.True(handler.UltimaRequisicao!.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task ApiAgent_expoe_ferramenta_e_prompt_do_dominio()
    {
        await using var agente = new ApiAgent("GET https://api.exemplo.com/itens");

        Assert.Equal("Teste de API", agente.DisplayName);
        Assert.Equal(TaskKind.Automation, agente.TaskKind);
        Assert.Single(agente.Tools.Tools);
        Assert.Contains("api.exemplo.com", agente.SystemPrompt);
        Assert.Contains("Quorum", agente.SystemPrompt);
    }

    // ---------------------------------------------------------------- MCP fake

    private sealed class FakeSession : IMcpSession
    {
        public bool FoiDescartada { get; private set; }

        public Task<IReadOnlyList<ToolDefinition>> ListToolsAsync(CancellationToken ct = default)
        {
            IReadOnlyList<ToolDefinition> t = new[]
            {
                new ToolDefinition("browser_navigate", "Navega", "{\"type\":\"object\",\"properties\":{}}")
            };
            return Task.FromResult(t);
        }

        public Task<McpToolResponse> CallToolAsync(string name, string argumentsJson, CancellationToken ct = default) =>
            Task.FromResult(new McpToolResponse("ok"));

        public ValueTask DisposeAsync()
        {
            FoiDescartada = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Agente_mcp_expoe_ferramentas_da_sessao()
    {
        var sessao = new FakeSession();
        await using var agente = await McpBackedAgent.FromSessionAsync(
            "Teste de Tela", "prompt do dominio", sessao);

        Assert.Equal("Teste de Tela", agente.DisplayName);
        Assert.Single(agente.Tools.Tools);
        Assert.Equal("browser_navigate", agente.Tools.Tools[0].Name);
    }

    [Fact]
    public async Task Descartar_o_agente_encerra_a_sessao_mcp()
    {
        var sessao = new FakeSession();
        var agente = await McpBackedAgent.FromSessionAsync("X", "p", sessao);

        await agente.DisposeAsync();

        Assert.True(sessao.FoiDescartada);
    }

    [Fact]
    public async Task ScreenAgent_exige_url()
    {
        // Valida ANTES de subir o servidor: nao adianta gastar tempo iniciando o
        // navegador para descobrir que falta a URL.
        await Assert.ThrowsAsync<ArgumentException>(() => ScreenAgent.CreateAsync("  "));
    }

    [Fact]
    public async Task DatabaseAgent_exige_conexao()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => DatabaseAgent.CreateRelationalAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => DatabaseAgent.CreateMongoAsync(""));
    }

    [Fact]
    public async Task Agente_roda_no_loop_de_ponta_a_ponta()
    {
        // Integracao: agente concreto + loop generico, sem IA real e sem rede.
        var handler = new FakeHandler(HttpStatusCode.OK, """{"ok":true}""");
        await using var agente = new ApiAgent(http: new HttpClient(handler));
        var provider = new Fakes.ScriptedProvider(
            new CompletionResponse("", new[] { ChamadaHttp(
                """{"metodo":"GET","url":"https://api.exemplo.com"}""") }),
            new CompletionResponse("A API respondeu 200 com ok=true.", Array.Empty<ToolCall>()));
        var loop = new AgentLoop(provider);

        var r = await loop.RunAsync("m", agente.SystemPrompt, "valide o endpoint", agente.Tools);

        Assert.Equal(AgentStopReason.Completed, r.StopReason);
        Assert.Contains("200", r.FinalText);
    }
}
