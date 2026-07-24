using Quorum.Core.Models;

namespace Quorum.Agents;

/// <summary>
/// Agente de teste de API: a IA monta e dispara requisicoes HTTP e analisa as
/// respostas. Nao depende de MCP nem de Node — e o agente mais leve, bom para
/// comecar uma validacao.
/// </summary>
public sealed class ApiAgent : IQuorumAgent
{
    private readonly HttpToolExecutor _tools;

    /// <param name="requisicaoBase">
    /// Requisicao que o usuario montou na interface (metodo, URL, headers, corpo).
    /// Entra no prompt como ponto de partida; a IA pode ajusta-la conforme o objetivo.
    /// </param>
    /// <param name="http">Cliente HTTP; nulo cria um proprio.</param>
    public ApiAgent(string? requisicaoBase = null, HttpClient? http = null)
    {
        _tools = new HttpToolExecutor(http);
        SystemPrompt = MontarPrompt(requisicaoBase);
    }

    public string DisplayName => "Teste de API";

    public string SystemPrompt { get; }

    public IToolExecutor Tools => _tools;

    public TaskKind TaskKind => TaskKind.Automation;

    public ValueTask DisposeAsync()
    {
        _tools.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string MontarPrompt(string? requisicaoBase)
    {
        var contexto = string.IsNullOrWhiteSpace(requisicaoBase)
            ? string.Empty
            : $"\n\nRequisicao base informada pelo usuario:\n{requisicaoBase}\n";

        return $"""
            {AgentPrompts.Persona}

            Voce esta testando uma API HTTP. Use a ferramenta {HttpToolExecutor.ToolName}
            para executar as chamadas — pode ajustar metodo, URL, headers e corpo
            conforme o objetivo do teste.{contexto}

            Analise status, headers e corpo da resposta, e avalie se a API se
            comportou como esperado. Um status de erro (4xx/5xx) nao e
            necessariamente falha do teste: pode ser exatamente o comportamento
            esperado para a entrada enviada. {AgentPrompts.OnToolError}

            {AgentPrompts.Reporting}
            """;
    }
}
