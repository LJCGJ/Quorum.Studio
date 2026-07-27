using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Quorum.Agents;
using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Core.Routing;
using Quorum.Providers;
using Quorum.Security;

namespace Quorum.App.Services;

/// <summary>Uma chave cadastrada, ja com o provedor reconhecido.</summary>
public sealed class ApiKeyEntry
{
    public ApiKeyEntry(string rawKey)
    {
        RawKey = rawKey.Trim();
        Provider = ModelRegistry.DetectProvider(RawKey);
        Masked = Mascarar(RawKey);
    }

    /// <summary>Chave completa. Fica so em memoria nesta sessao.</summary>
    public string RawKey { get; }

    public AiProvider Provider { get; }

    /// <summary>Versao mascarada, para exibir sem expor a chave.</summary>
    public string Masked { get; }

    public string Display => $"{Provider} — {Masked}";

    private static string Mascarar(string chave) =>
        chave.Length <= 12 ? "••••"
        : $"{chave[..6]}••••••{chave[^4..]}";
}

/// <summary>
/// Estado da sessao: chaves de API, preferencias de roteamento, catalogo de
/// modelos e o gerente de tarefas simultaneas.
///
/// Suporta VARIAS chaves ao mesmo tempo — uma por provedor. E o que viabiliza
/// tarefas simultaneas atendidas por IAs diferentes: enquanto o Claude manipula
/// um banco, o Gemini pode conversar. O roteador so considera modelos dos
/// provedores que tem chave cadastrada.
///
/// As chaves vivem em MEMORIA, de proposito — o cofre por sistema operacional
/// chega na Fase D. Fechar o app as apaga.
/// </summary>
public sealed class SessionService
{
    public SessionService()
    {
        Registry = new ModelRegistry(DefaultCatalog.Models);
        Router = new ModelRouter(Registry);
        Sessions = new AgentSessionManager(maxSimultaneas: 3);
        Keys = new ObservableCollection<ApiKeyEntry>();
        Files = new FileSaveService();
        _vault = new FileSecretVault();
        CarregarChavesSalvas();
        _preferences = RoutingPreferences.Default;
    }

    public ModelRegistry Registry { get; }
    public ModelRouter Router { get; }
    public AgentSessionManager Sessions { get; }

    /// <summary>Salva scripts e relatorios em disco.</summary>
    public FileSaveService Files { get; }

    private readonly ISecretVault _vault;

    /// <summary>Onde as chaves ficam guardadas, para o usuario poder conferir.</summary>
    public string VaultLocation => _vault.Location;

    /// <summary>
    /// Se as chaves guardadas estao cifradas pelo sistema operacional. Falso em
    /// Linux e macOS, onde a protecao vem so das permissoes do arquivo — a
    /// interface conta isso ao usuario em vez de prometer mais do que entrega.
    /// </summary>
    public bool VaultIsEncrypted => _vault.Protection == VaultProtection.OperatingSystem;

    /// <summary>
    /// Lembrar as chaves entre execucoes. Desligar apaga o que estiver guardado —
    /// desmarcar e um pedido para esquecer, nao so para parar de gravar.
    /// </summary>
    public bool RememberKeys
    {
        get => _rememberKeys;
        set
        {
            _rememberKeys = value;
            if (value) PersistirChaves();
            else _vault.Clear();
        }
    }

    private bool _rememberKeys;

    /// <summary>Chaves cadastradas, no maximo uma por provedor.</summary>
    public ObservableCollection<ApiKeyEntry> Keys { get; }

    private RoutingPreferences _preferences;

    /// <summary>
    /// Preferencias de roteamento. A lista de provedores disponiveis e sempre
    /// derivada das chaves cadastradas — quem escreve nao precisa lembrar disso.
    /// </summary>
    public RoutingPreferences Preferences
    {
        get => _preferences with { AvailableProviders = ProvidersDisponiveis };
        set => _preferences = value;
    }

    public bool HasApiKey => Keys.Count > 0;

    private IReadOnlySet<AiProvider> ProvidersDisponiveis =>
        Keys.Select(k => k.Provider).ToHashSet();

    /// <summary>
    /// Cadastra uma chave. Se ja houver uma do mesmo provedor, substitui — manter
    /// duas chaves do mesmo provedor so criaria ambiguidade sobre qual usar.
    /// </summary>
    public ApiKeyEntry AddKey(string rawKey)
    {
        var entrada = new ApiKeyEntry(rawKey);

        var existente = Keys.FirstOrDefault(k => k.Provider == entrada.Provider);
        if (existente is not null) Keys.Remove(existente);

        Keys.Add(entrada);
        if (RememberKeys) _vault.Save(entrada.Provider.ToString(), entrada.RawKey);
        return entrada;
    }

    public void RemoveKey(ApiKeyEntry entrada)
    {
        Keys.Remove(entrada);
        if (RememberKeys) _vault.Delete(entrada.Provider.ToString());
    }

    /// <summary>Le as chaves guardadas na execucao anterior, se houver.</summary>
    private void CarregarChavesSalvas()
    {
        foreach (var nome in _vault.List())
        {
            var chave = _vault.Load(nome);
            if (string.IsNullOrWhiteSpace(chave)) continue;

            try { Keys.Add(new ApiKeyEntry(chave)); }
            catch (ArgumentException) { /* entrada invalida: ignora */ }
        }

        // Se havia algo guardado, e porque o usuario pediu para lembrar.
        _rememberKeys = Keys.Count > 0;
    }

    private void PersistirChaves()
    {
        _vault.Clear();
        foreach (var k in Keys) _vault.Save(k.Provider.ToString(), k.RawKey);
    }

    /// <summary>
    /// Consulta os provedores cadastrados e substitui o catalogo pelo que eles
    /// realmente oferecem hoje. Modelos aposentados somem; lancamentos aparecem.
    /// Devolve quantos modelos entraram e os avisos de provedores que falharam —
    /// a falha de um nao impede os outros de atualizar.
    /// </summary>
    public async Task<(int Total, IReadOnlyList<string> Avisos)> RefreshCatalogAsync(
        CancellationToken ct = default)
    {
        var encontrados = new List<ModelInfo>();
        var avisos = new List<string>();

        using var buscador = new ModelCatalogFetcher();

        foreach (var chave in Keys.ToList())
        {
            try
            {
                var modelos = await buscador.FetchAsync(chave.RawKey, ct).ConfigureAwait(false);
                encontrados.AddRange(modelos);
            }
            catch (Exception ex)
            {
                avisos.Add($"{chave.Provider}: {ex.Message}");
            }
        }

        // So substitui se algo veio: uma falha geral nao pode deixar o app sem
        // nenhum modelo para oferecer.
        if (encontrados.Count > 0)
            Registry.Replace(encontrados);

        return (encontrados.Count, avisos);
    }

    /// <summary>Chave do provedor pedido; nula se nao houver.</summary>
    public string? KeyFor(AiProvider provider) =>
        Keys.FirstOrDefault(k => k.Provider == provider)?.RawKey;

    /// <summary>
    /// Cria o provider certo para um modelo, usando a chave DAQUELE provedor.
    ///
    /// E o ponto que fecha o buraco: a fabrica detecta o provedor pela chave, entao
    /// passar a chave errada produziria um cliente de um provedor pedindo o modelo
    /// de outro. Aqui a chave e escolhida a partir do modelo.
    /// </summary>
    public IAiProvider CreateProviderFor(string modelId)
    {
        var modelo = Registry.FindById(modelId)
            ?? throw new InvalidOperationException(
                $"Modelo '{modelId}' nao esta no catalogo.");

        var chave = KeyFor(modelo.Provider)
            ?? throw new InvalidOperationException(
                $"Nao ha chave de API cadastrada para {modelo.Provider}.");

        return AiProviderFactory.Create(chave, modelId);
    }
}
