using Quorum.Core.Ai;
using Quorum.Core.Models;

namespace Quorum.Agents;

/// <summary>
/// Agente revisor: le criticamente o relatorio de outra IA e aponta o que nao se
/// sustenta.
///
/// Nao usa ferramentas — nao navega, nao consulta banco, nao repete o teste. E
/// analise de texto sobre o que ja foi produzido, o que a torna bem mais barata
/// que a tarefa original (uma chamada, sem os varios passos do loop agentic).
/// Ainda assim CUSTA, e a interface diz isso antes de o usuario acionar.
/// </summary>
public sealed class ReviewAgent : IQuorumAgent
{
    public ReviewAgent(string objetivoOriginal, string relatorio, string? autor = null)
    {
        Objective = MontarObjetivo(objetivoOriginal, relatorio, autor);
    }

    public string DisplayName => "Revisao";

    /// <summary>Texto que vai como mensagem do usuario para o revisor.</summary>
    public string Objective { get; }

    public string SystemPrompt => $"""
        {AgentPrompts.Persona}

        Sua funcao agora e REVISAR criticamente o trabalho de outro agente, como um
        engenheiro senior faria numa revisao de par. Voce nao executou o teste e nao
        tem como reexecuta-lo: julgue apenas o que esta escrito.

        Procure especificamente por:
        - conclusoes que o relatorio nao sustenta com evidencia;
        - passos que faltaram para o objetivo ser considerado cumprido;
        - riscos e casos de borda que o teste deixou de fora;
        - problemas no script gerado (seletores fragis, ausencia de espera,
          dados fixos que quebram em outro ambiente, falta de assercao real).

        Seja util, nao severo por esporte: se o trabalho esta correto, diga isso em
        uma linha e concentre o resto no que ainda pode ser melhorado. Comece com um
        veredito curto (uma frase) e depois liste os pontos, do mais importante para
        o menos. Se sugerir correcao de codigo, use blocos ```linguagem.
        """;

    /// <summary>Revisao nao usa ferramentas: so leitura e julgamento.</summary>
    public IToolExecutor Tools { get; } = new SemFerramentas();

    /// <summary>Analise: o roteador prioriza capacidade e contexto para este tipo.</summary>
    public TaskKind TaskKind => TaskKind.Analysis;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string MontarObjetivo(string objetivo, string relatorio, string? autor)
    {
        var assinatura = string.IsNullOrWhiteSpace(autor)
            ? "outro agente"
            : $"o modelo {autor}";

        return $"""
            O objetivo pedido pelo usuario era:
            {objetivo}

            Este e o relatorio produzido por {assinatura}:
            ---
            {relatorio}
            ---

            Revise o relatorio acima seguindo suas instrucoes.
            """;
    }

    /// <summary>Executor vazio: qualquer chamada de ferramenta aqui seria um engano.</summary>
    private sealed class SemFerramentas : IToolExecutor
    {
        public IReadOnlyList<ToolDefinition> Tools { get; } = Array.Empty<ToolDefinition>();

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A revisao nao expoe ferramentas.");
    }
}
