using Quorum.Core.Models;

namespace Quorum.Core.Routing;

/// <summary>
/// Resultado de uma tentativa de roteamento. Ou traz o modelo escolhido, ou
/// explica por que nenhum servia — sem lancar excecao para fluxo esperado.
/// </summary>
public sealed record RoutingResult
{
    /// <summary>Modelo escolhido; nulo quando <see cref="Success"/> e falso.</summary>
    public ModelInfo? Selected { get; private init; }

    /// <summary>Motivo legivel quando nenhum modelo serve; vazio no sucesso.</summary>
    public string Reason { get; private init; } = string.Empty;

    /// <summary>Se o roteamento encontrou um modelo adequado.</summary>
    public bool Success => Selected is not null;

    public static RoutingResult Ok(ModelInfo model) => new() { Selected = model };

    public static RoutingResult Fail(string reason) => new() { Reason = reason };
}
