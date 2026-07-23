using System.Text.Json;
using Microsoft.Extensions.AI;
using Quorum.Core.Ai;

namespace Quorum.Providers;

/// <summary>
/// Uma <see cref="AIFunction"/> que apenas DECLARA uma ferramenta (nome, descricao
/// e schema JSON dos parametros) para o modelo. A execucao NAO acontece aqui — quem
/// executa e o <c>AgentLoop</c> via <c>IToolExecutor</c>, ligando a ferramenta ao
/// mundo real (navegador, banco, HTTP). Por isso <see cref="InvokeCoreAsync"/> nunca
/// deve ser chamada neste fluxo; se for, lanca para deixar o erro obvio.
///
/// Essa separacao (declarar aqui, executar no loop) e o que mantem o adaptador
/// generico: o mesmo caminho serve para qualquer ferramenta, de qualquer agente.
/// </summary>
internal sealed class QuorumDeclaredFunction : AIFunction
{
    private readonly JsonElement _schema;

    public QuorumDeclaredFunction(ToolDefinition definition)
    {
        Name = definition.Name;
        Description = definition.Description;
        _schema = ParseSchema(definition.ParametersJsonSchema);
    }

    public override string Name { get; }

    public override string Description { get; }

    public override JsonElement JsonSchema => _schema;

    protected override ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // Este caminho nunca deve ser exercido: a execucao real e do AgentLoop.
        throw new NotSupportedException(
            $"A ferramenta '{Name}' e apenas declarativa aqui; a execucao e feita " +
            "pelo AgentLoop via IToolExecutor.");
    }

    private static JsonElement ParseSchema(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        try
        {
            return JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            // Schema invalido nao pode derrubar a montagem da chamada: cai para um
            // objeto vazio, que a maioria dos provedores aceita.
            return JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        }
    }
}
