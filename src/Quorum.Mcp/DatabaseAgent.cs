using Quorum.Agents;

namespace Quorum.Mcp;

/// <summary>
/// Agentes de banco de dados. Relacionais vao pelo DBHub; MongoDB tem servidor
/// proprio. Em ambos, somente leitura e o padrao — e o prompt diz a IA em que
/// modo ela esta, para nao tentar escrever num banco travado.
/// </summary>
public static class DatabaseAgent
{
    /// <param name="dsn">postgres://... | mysql://... | sqlite:///caminho.db | sqlserver://...</param>
    /// <param name="somenteLeitura">Padrao true: so consultas SELECT.</param>
    public static Task<McpBackedAgent> CreateRelationalAsync(
        string dsn,
        bool somenteLeitura = true,
        Action<string>? onServerLog = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dsn))
            throw new ArgumentException("A string de conexao e obrigatoria.", nameof(dsn));

        return McpBackedAgent.CreateAsync(
            "Banco de Dados",
            MontarPrompt(somenteLeitura, "SELECT", "INSERT/UPDATE/DELETE/DROP"),
            McpServerSpec.DbHub(dsn, somenteLeitura),
            onServerLog, ct: ct);
    }

    /// <param name="connectionString">mongodb://usuario:senha@host:porta/banco</param>
    /// <param name="somenteLeitura">
    /// Padrao true. Importante: o servidor oficial do MongoDB e leitura E escrita
    /// por padrao, entao a restricao precisa ser passada explicitamente.
    /// </param>
    public static Task<McpBackedAgent> CreateMongoAsync(
        string connectionString,
        bool somenteLeitura = true,
        Action<string>? onServerLog = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A string de conexao e obrigatoria.", nameof(connectionString));

        var prompt = MontarPrompt(somenteLeitura, "consultas", "insert/update/delete/drop") +
            "\n\nNao use ferramentas administrativas do Atlas (criar usuarios, alterar " +
            "lista de IPs, gerenciar clusters): limite-se a explorar e consultar os dados.";

        return McpBackedAgent.CreateAsync(
            "MongoDB", prompt, McpServerSpec.MongoDb(connectionString, somenteLeitura),
            onServerLog, ct: ct);
    }

    private static string MontarPrompt(bool somenteLeitura, string permitido, string destrutivo)
    {
        var modo = somenteLeitura
            ? $"A conexao esta em modo SOMENTE LEITURA: apenas {permitido} sao permitidos. " +
              "Nao tente escrever; se o objetivo exigir escrita, diga isso ao usuario."
            : $"A conexao permite leitura e escrita. Seja cuidadoso com operacoes " +
              $"destrutivas ({destrutivo}) e confirme com o usuario antes de executa-las.";

        return $"""
            {AgentPrompts.Persona}

            Voce esta conectado a um banco de dados. {modo}

            Explore o schema antes de consultar: liste as tabelas ou colecoes e
            observe as colunas e os tipos, para escrever consultas que funcionem de
            primeira. {AgentPrompts.OnToolError}

            {AgentPrompts.Reporting}
            """;
    }
}
