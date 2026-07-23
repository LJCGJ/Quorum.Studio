namespace Quorum.Core.Models;

/// <summary>
/// Preferencias do usuario que influenciam o roteamento. Vem da tela de
/// Configuracoes (aba de roteamento). Imutavel.
/// </summary>
/// <param name="EconomyMode">
/// Se verdadeiro, o roteador nunca sobe de tier sem necessidade real — prioriza
/// o modelo mais barato que ainda atende os requisitos da tarefa.
/// </param>
/// <param name="PinnedModelId">
/// Se preenchido, forca o uso deste modelo especifico (override do usuario),
/// desde que ele exista no registro e atenda os requisitos obrigatorios da
/// tarefa (ex.: tool-use). Nulo = deixar o roteador decidir.
/// </param>
/// <param name="PreferredProvider">
/// Provedor preferido como desempate quando varios modelos servem igualmente.
/// Nulo = sem preferencia.
/// </param>
public sealed record RoutingPreferences(
    bool EconomyMode = false,
    string? PinnedModelId = null,
    AiProvider? PreferredProvider = null)
{
    /// <summary>Preferencias padrao: sem economia forcada, sem pin, sem provedor preferido.</summary>
    public static RoutingPreferences Default { get; } = new();
}
