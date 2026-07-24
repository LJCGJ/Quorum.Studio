using Quorum.Agents;

namespace Quorum.Mcp;

/// <summary>
/// Agente de teste de tela: dirige um navegador real via Playwright MCP,
/// observando o estado da pagina antes de cada acao.
/// </summary>
public static class ScreenAgent
{
    /// <param name="urlAlvo">Endereco onde o teste comeca.</param>
    /// <param name="headless">Sem janela visivel. Padrao false: ver o teste rodando ajuda.</param>
    /// <param name="onServerLog">Progresso do servidor, para exibir na interface.</param>
    public static Task<McpBackedAgent> CreateAsync(
        string urlAlvo,
        bool headless = false,
        Action<string>? onServerLog = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlAlvo))
            throw new ArgumentException("A URL alvo e obrigatoria.", nameof(urlAlvo));

        var prompt = $"""
            {AgentPrompts.Persona}

            Voce controla um navegador real. Comece navegando ate {urlAlvo}.

            Antes de cada acao, observe o estado atual da pagina (tire um snapshot
            da estrutura) e so entao interaja: a pagina pode carregar conteudo por
            JavaScript e mudar entre um passo e outro. Prefira identificar
            elementos pelo que o usuario ve (texto, rotulo, papel) em vez de
            seletores fragis. {AgentPrompts.OnToolError}

            {AgentPrompts.Reporting}
            """;

        return McpBackedAgent.CreateAsync(
            "Teste de Tela", prompt, McpServerSpec.Playwright(headless), onServerLog, ct: ct);
    }
}
