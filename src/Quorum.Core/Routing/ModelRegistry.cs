using Quorum.Core.Models;

namespace Quorum.Core.Routing;

/// <summary>
/// Catalogo consultavel dos modelos disponiveis. Na v5 a lista e populada
/// dinamicamente a partir dos provedores (como o botao "Buscar modelos" da v4.2),
/// mas o registro em si e apenas armazenamento + consulta, sem IO — o que o torna
/// testavel de forma isolada.
///
/// THREAD-SAFETY: o registro e lido pela UI enquanto a busca assincrona de
/// modelos pode estar substituindo o catalogo. Todas as operacoes sao
/// protegidas por lock, e <see cref="All"/> devolve um SNAPSHOT imutavel —
/// nunca a lista interna — para que uma enumeracao em andamento jamais quebre
/// com "collection was modified".
/// </summary>
public sealed class ModelRegistry
{
    private readonly object _gate = new();
    private readonly List<ModelInfo> _models = new();

    public ModelRegistry(IEnumerable<ModelInfo>? seed = null)
    {
        if (seed is not null)
            _models.AddRange(seed);
    }

    /// <summary>Snapshot imutavel de todos os modelos conhecidos, em ordem de insercao.</summary>
    public IReadOnlyList<ModelInfo> All
    {
        get { lock (_gate) return _models.ToArray(); }
    }

    /// <summary>Substitui todo o catalogo (usado apos buscar a lista no provedor).</summary>
    public void Replace(IEnumerable<ModelInfo> models)
    {
        // Materializa FORA do lock (a origem pode ser lenta/lazy)
        var novos = models.ToList();
        lock (_gate)
        {
            _models.Clear();
            _models.AddRange(novos);
        }
    }

    /// <summary>Adiciona um modelo se ainda nao houver outro com o mesmo Id.</summary>
    public void AddOrIgnore(ModelInfo model)
    {
        lock (_gate)
        {
            if (_models.All(m => m.Id != model.Id))
                _models.Add(model);
        }
    }

    /// <summary>Busca um modelo pelo Id exato; nulo se nao existir.</summary>
    public ModelInfo? FindById(string id)
    {
        lock (_gate) return _models.FirstOrDefault(m => m.Id == id);
    }

    /// <summary>Modelos de um provedor especifico (snapshot).</summary>
    public IReadOnlyList<ModelInfo> ForProvider(AiProvider provider)
    {
        lock (_gate) return _models.Where(m => m.Provider == provider).ToArray();
    }

    /// <summary>
    /// Detecta o provedor pelo prefixo da chave de API — mesma heuristica da v4.2:
    /// "sk-ant-" e Claude; "sk-" e OpenAI; qualquer outro (AIza, AQ., ...) e Gemini,
    /// que fica como padrao porque o formato da chave do Google ja mudou uma vez
    /// (AIza -> AQ.) e validar so um prefixo quebraria com chaves novas.
    /// </summary>
    public static AiProvider DetectProvider(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Chave de API vazia.", nameof(apiKey));

        var k = apiKey.Trim();
        if (k.StartsWith("sk-ant-", StringComparison.Ordinal)) return AiProvider.Claude;
        if (k.StartsWith("sk-", StringComparison.Ordinal)) return AiProvider.OpenAI;
        return AiProvider.Gemini;
    }
}
