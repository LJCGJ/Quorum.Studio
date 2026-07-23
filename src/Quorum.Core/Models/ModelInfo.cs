namespace Quorum.Core.Models;

/// <summary>
/// Metadados de um modelo de IA, usados pelo roteador para escolher o modelo
/// certo para cada tarefa. A lista de modelos e carregada dinamicamente do
/// provedor (evita que um modelo aposentado quebre o app, como ocorria na v4.2).
/// </summary>
/// <param name="Provider">Provedor que serve este modelo.</param>
/// <param name="Id">Identificador exato usado na API (ex.: "claude-haiku-4-5-20251001").</param>
/// <param name="DisplayName">Nome amigavel para exibir na interface.</param>
/// <param name="Tier">Faixa de capacidade x custo.</param>
/// <param name="SupportsTools">Se o modelo aceita tool-use (obrigatorio para automacao).</param>
/// <param name="ContextWindow">Janela de contexto em tokens.</param>
/// <param name="CostInputPerMillion">Custo por milhao de tokens de entrada (USD).</param>
/// <param name="CostOutputPerMillion">Custo por milhao de tokens de saida (USD).</param>
public sealed record ModelInfo(
    AiProvider Provider,
    string Id,
    string DisplayName,
    ModelTier Tier,
    bool SupportsTools,
    int ContextWindow,
    decimal CostInputPerMillion,
    decimal CostOutputPerMillion);
