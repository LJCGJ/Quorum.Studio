using Quorum.Core.Models;

namespace Quorum.Agents;

/// <summary>
/// Um agente concreto: a combinacao de um conjunto de ferramentas com a instrucao
/// de sistema do seu dominio.
///
/// O <see cref="AgentLoop"/> e generico e nao sabe se esta dirigindo um navegador
/// ou consultando um banco; e o agente que traz esse conhecimento. Cada
/// implementacao tambem e responsavel por traduzir as falhas OPERACIONAIS do seu
/// dominio em resultados de erro (ver nota no McpToolExecutor), deixando o
/// classificador generico so como ultima linha.
/// </summary>
public interface IQuorumAgent : IAsyncDisposable
{
    /// <summary>Nome legivel, para a interface e os relatorios.</summary>
    string DisplayName { get; }

    /// <summary>Instrucao de sistema com a persona e as regras do dominio.</summary>
    string SystemPrompt { get; }

    /// <summary>Ferramentas que este agente oferece a IA.</summary>
    IToolExecutor Tools { get; }

    /// <summary>Tipo de tarefa, para o roteador escolher o modelo adequado.</summary>
    TaskKind TaskKind { get; }
}

/// <summary>
/// Trechos de instrucao comuns a todos os agentes. Ficam num lugar so para as
/// regras nao divergirem entre dominios — foi assim que, na v4.2, o Gemini acabou
/// sem a regra dos blocos de codigo.
/// </summary>
public static class AgentPrompts
{
    /// <summary>Identidade compartilhada.</summary>
    public const string Persona =
        "Voce e o Quorum, assistente especialista em automacao de testes, qualidade " +
        "de software (QA) e seguranca. Trabalhe como um engenheiro senior: direto, " +
        "pratico, sem marketing.";

    /// <summary>Como relatar e como entregar codigo reaproveitavel.</summary>
    public const string Reporting =
        "Ao final, escreva um relatorio claro do que testou e do que encontrou. Se " +
        "fizer sentido gerar um script que reproduza o teste, prefira Robot Framework " +
        "ou Python (padrao em QA) e coloque o codigo em blocos ```linguagem para o " +
        "sistema conseguir extrair e salvar. Se o objetivo nao for possivel no " +
        "ambiente dado, diga isso com clareza em vez de inventar um resultado.";

    /// <summary>Como reagir quando uma ferramenta falha.</summary>
    public const string OnToolError =
        "Quando uma ferramenta retornar erro, leia a mensagem e ajuste a abordagem " +
        "em vez de repetir a mesma chamada.";
}
