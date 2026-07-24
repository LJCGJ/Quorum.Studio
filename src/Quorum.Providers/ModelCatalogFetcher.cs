using System.Net.Http.Headers;
using System.Text.Json;
using Quorum.Core.Models;
using Quorum.Core.Routing;

namespace Quorum.Providers;

/// <summary>
/// Busca a lista de modelos DIRETO no provedor.
///
/// Existe porque modelos sao aposentados sem aviso — no app anterior isso quebrou
/// a ferramenta duas vezes, com o usuario recebendo erro de API por um nome fixo
/// no codigo. Consultando o provedor, um modelo aposentado simplesmente some da
/// lista e um lancamento novo aparece sozinho.
///
/// Usa HTTP direto (e nao os SDKs) porque cada SDK expoe a listagem de um jeito, e
/// aqui o formato de resposta e simples e estavel nos tres.
/// </summary>
public sealed class ModelCatalogFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <param name="http">Cliente a usar; nulo cria um proprio (nos testes, injete um falso).</param>
    public ModelCatalogFetcher(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Consulta os modelos do provedor dono da chave.
    /// </summary>
    /// <param name="apiKey">Chave; o provedor e detectado pelo prefixo.</param>
    public async Task<IReadOnlyList<ModelInfo>> FetchAsync(
        string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Chave de API vazia.", nameof(apiKey));

        var provider = ModelRegistry.DetectProvider(apiKey);
        using var req = MontarRequisicao(provider, apiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var corpo = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new ModelFetchException(Explicar(provider, (int)resp.StatusCode));

        var ids = ExtrairIds(provider, corpo);
        return ids.Select(id => Descrever(provider, id)).ToList();
    }

    private static HttpRequestMessage MontarRequisicao(AiProvider provider, string apiKey)
    {
        switch (provider)
        {
            case AiProvider.Claude:
                var anthropic = new HttpRequestMessage(HttpMethod.Get,
                    "https://api.anthropic.com/v1/models?limit=100");
                anthropic.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                anthropic.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                return anthropic;

            case AiProvider.OpenAI:
                var openai = new HttpRequestMessage(HttpMethod.Get,
                    "https://api.openai.com/v1/models");
                openai.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return openai;

            default: // Gemini
                return new HttpRequestMessage(HttpMethod.Get,
                    $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}&pageSize=200");
        }
    }

    /// <summary>Extrai os identificadores, respeitando o formato de cada provedor.</summary>
    private static List<string> ExtrairIds(AiProvider provider, string json)
    {
        var ids = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            // Claude e OpenAI: { "data": [ { "id": ... } ] }
            // Gemini:          { "models": [ { "name": "models/gemini-..." } ] }
            var lista = provider == AiProvider.Gemini ? "models" : "data";
            if (!raiz.TryGetProperty(lista, out var itens) || itens.ValueKind != JsonValueKind.Array)
                return ids;

            foreach (var item in itens.EnumerateArray())
            {
                if (provider == AiProvider.Gemini)
                {
                    if (!item.TryGetProperty("name", out var nome)) continue;
                    var bruto = nome.GetString() ?? "";
                    // "models/gemini-2.5-flash" -> "gemini-2.5-flash"
                    var id = bruto.StartsWith("models/", StringComparison.Ordinal)
                        ? bruto["models/".Length..] : bruto;

                    // So interessam os que geram texto: a API tambem lista modelos
                    // de embedding, que nao servem para conversa nem automacao.
                    if (item.TryGetProperty("supportedGenerationMethods", out var metodos) &&
                        metodos.ValueKind == JsonValueKind.Array &&
                        !metodos.EnumerateArray().Any(m =>
                            m.GetString() is "generateContent" or "streamGenerateContent"))
                        continue;

                    if (id.Length > 0) ids.Add(id);
                }
                else
                {
                    if (item.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s)
                        ids.Add(s);
                }
            }
        }
        catch (JsonException ex)
        {
            throw new ModelFetchException(
                "A resposta do provedor nao veio no formato esperado. " +
                "Pode ser uma mudanca na API — tente de novo mais tarde.", ex);
        }

        return ids;
    }

    /// <summary>
    /// Monta o <see cref="ModelInfo"/> de um id. Quando o modelo ja e conhecido,
    /// reaproveita a tabela de precos e caracteristicas; quando e novo, infere o
    /// basico pelo nome e marca o preco como desconhecido — melhor admitir do que
    /// inventar um numero em que o usuario vai confiar para decidir custo.
    /// </summary>
    private static ModelInfo Descrever(AiProvider provider, string id)
    {
        var conhecido = DefaultCatalog.Models.FirstOrDefault(m => m.Id == id);
        if (conhecido is not null) return conhecido;

        var nome = id.ToLowerInvariant();

        var tier = nome.Contains("opus") || nome.Contains("pro") ? ModelTier.Powerful
            : nome.Contains("haiku") || nome.Contains("mini") || nome.Contains("lite")
              || nome.Contains("flash") || nome.Contains("nano") ? ModelTier.Fast
            : ModelTier.Balanced;

        // Janela: valor tipico do provedor, corrigido quando o id indica algo maior.
        var contexto = provider switch
        {
            AiProvider.Gemini => 1_000_000,
            AiProvider.Claude => 200_000,
            _ => 128_000
        };

        return new ModelInfo(
            Provider: provider,
            Id: id,
            DisplayName: id,
            Tier: tier,
            SupportsTools: true,   // os modelos de texto atuais dos tres suportam
            ContextWindow: contexto,
            CostInputPerMillion: 0m,
            CostOutputPerMillion: 0m,
            PricingKnown: false);
    }

    private static string Explicar(AiProvider provider, int status) => status switch
    {
        401 or 403 => $"A chave de {provider} foi recusada. Confira se ela esta correta e ativa.",
        429 => $"O provedor {provider} recusou por excesso de requisicoes. Tente em alguns instantes.",
        >= 500 => $"O servico do provedor {provider} esta indisponivel no momento.",
        _ => $"O provedor {provider} respondeu com erro {status} ao listar os modelos."
    };

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}

/// <summary>Falha ao consultar a lista de modelos, com mensagem para o usuario.</summary>
public sealed class ModelFetchException : Exception
{
    public ModelFetchException(string message) : base(message) { }
    public ModelFetchException(string message, Exception inner) : base(message, inner) { }
}
