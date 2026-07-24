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
/// <param name="AvailableProviders">
/// Provedores para os quais existe chave de API utilizavel. O roteador so escolhe
/// modelos destes provedores.
///
/// Sem isso, o roteador pode eleger um modelo Gemini quando so ha chave do Claude:
/// a fabrica de providers detecta o provedor PELA CHAVE, entao criaria um cliente
/// Claude pedindo um modelo que ele nao conhece — falha na primeira chamada, com
/// credito ja gasto. Nulo ou vazio = sem restricao (util em testes).
/// </param>
public sealed record RoutingPreferences(
    bool EconomyMode = false,
    string? PinnedModelId = null,
    AiProvider? PreferredProvider = null,
    IReadOnlySet<AiProvider>? AvailableProviders = null)
{
    /// <summary>True se o provedor pode ser usado (ha chave para ele).</summary>
    public bool IsUsable(AiProvider provider) =>
        AvailableProviders is null || AvailableProviders.Contains(provider);

    /// <summary>Preferencias padrao: sem economia forcada, sem pin, sem provedor preferido.</summary>
    public static RoutingPreferences Default { get; } = new();
}
