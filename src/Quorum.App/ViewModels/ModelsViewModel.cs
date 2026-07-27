using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quorum.App.Services;
using Quorum.Core.Models;
using Quorum.Core.Routing;

namespace Quorum.App.ViewModels;

/// <summary>
/// A pagina de modelos: ver, atualizar e escolher.
///
/// Tudo que diz respeito a QUAL modelo responde vive aqui — a lista vinda do
/// provedor, a escolha manual e a simulacao do roteamento. Configuracoes cuida
/// das chaves e do custo. Antes isso estava dividido entre as duas telas, o que
/// obrigava a ir e voltar para uma decisao so.
/// </summary>
public sealed partial class ModelsViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly Action _onChanged;

    public ModelsViewModel(SessionService session, Action onChanged)
    {
        _session = session;
        _onChanged = onChanged;
        Models = new ObservableCollection<ModelRow>();
        FallbackChain = new ObservableCollection<string>();
        _selectedTaskKind = TaskKinds[0];
        Recarregar();
    }

    public override string Title => "Modelos";

    public override string Subtitle =>
        "Quem responde o que. Atualize a lista, escolha um modelo ou deixe o roteador decidir.";

    public ObservableCollection<ModelRow> Models { get; }

    public ObservableCollection<string> FallbackChain { get; }

    /// <summary>Tipo de tarefa com rotulo em portugues (o ComboBox exibe o ToString).</summary>
    public sealed record TaskOption(TaskKind Value, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<TaskOption> TaskKinds { get; } = new[]
    {
        new TaskOption(TaskKind.Chat, "Conversa"),
        new TaskOption(TaskKind.DomScan, "Leitura de pagina"),
        new TaskOption(TaskKind.Automation, "Automacao com ferramentas"),
        new TaskOption(TaskKind.Analysis, "Analise longa")
    };

    [ObservableProperty] private TaskOption _selectedTaskKind;
    [ObservableProperty] private string _decision = string.Empty;
    [ObservableProperty] private string _decisionReason = string.Empty;
    [ObservableProperty] private string _selectionLabel = string.Empty;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasKeys;

    partial void OnSelectedTaskKindChanged(TaskOption value) => Simular();

    /// <summary>
    /// Busca a lista direto nos provedores cadastrados. Modelos aposentados somem,
    /// lancamentos aparecem — e nada disso consome tokens.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_session.HasApiKey)
        {
            Message = "Cadastre uma chave em Configuracoes para buscar os modelos.";
            return;
        }

        IsRefreshing = true;
        Message = "Consultando os provedores...";

        try
        {
            var (total, avisos) = await _session.RefreshCatalogAsync().ConfigureAwait(true);

            Message = total > 0
                ? $"{total} modelos carregados direto do provedor."
                : "O provedor nao retornou modelos.";

            if (avisos.Count > 0)
                Message += "  " + string.Join("  ", avisos);

            Recarregar();
        }
        catch (Exception ex)
        {
            Message = $"Nao foi possivel atualizar: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Fixa o modelo da linha: todas as tarefas passam a usa-lo.</summary>
    [RelayCommand]
    private void Use(ModelRow? row)
    {
        if (row is null) return;

        if (!row.HasKey)
        {
            Message = $"Cadastre uma chave de {row.Provider} para usar este modelo.";
            return;
        }

        _session.Preferences = _session.Preferences with { PinnedModelId = row.Id };
        Message = $"{row.Name} passa a atender todas as tarefas.";
        Recarregar();
        _onChanged();
    }

    /// <summary>Devolve a escolha ao roteador.</summary>
    [RelayCommand]
    private void UseAutomatic()
    {
        _session.Preferences = _session.Preferences with { PinnedModelId = null };
        Message = "O roteador volta a escolher o modelo por tarefa.";
        Recarregar();
        _onChanged();
    }

    /// <summary>Relê o catalogo e recalcula o que a tela mostra.</summary>
    public void Recarregar()
    {
        var prefs = _session.Preferences;
        HasKeys = _session.HasApiKey;

        Models.Clear();
        foreach (var m in _session.Registry.All)
            Models.Add(new ModelRow(m, prefs.IsUsable(m.Provider), m.Id == prefs.PinnedModelId));

        IsPinned = prefs.PinnedModelId is not null;
        SelectionLabel = IsPinned
            ? $"Fixado: {prefs.PinnedModelId}"
            : "Automatico — o roteador escolhe por tarefa";

        Simular();
    }

    /// <summary>
    /// Mostra a decisao do roteador para o tipo de tarefa escolhido. Roda no
    /// <c>ModelRouter</c> (funcao pura, sem rede): resposta imediata e sem custo.
    /// </summary>
    private void Simular()
    {
        var tarefa = new TaskDescriptor(SelectedTaskKind.Value);
        var prefs = _session.Preferences;
        var resultado = _session.Router.Route(tarefa, prefs);

        FallbackChain.Clear();

        if (!resultado.Success)
        {
            Decision = "Nenhum modelo disponivel";
            DecisionReason = resultado.Reason;
            return;
        }

        var m = resultado.Selected!;
        Decision = $"{m.DisplayName}  ·  {m.Provider}";
        DecisionReason = prefs.PinnedModelId is not null
            ? "Voce fixou este modelo, entao ele atende qualquer tipo de tarefa."
            : Explicar(SelectedTaskKind.Value, prefs.EconomyMode, m);

        foreach (var alt in _session.Router.RouteWithFallback(tarefa, prefs).Skip(1))
            FallbackChain.Add(alt.DisplayName);
    }

    private static string Explicar(TaskKind kind, bool economy, ModelInfo model)
    {
        if (economy)
            return "Economia ligada: escolhi o mais barato que atende a tarefa.";

        return kind switch
        {
            TaskKind.Chat => "Conversa nao usa ferramentas: um modelo rapido e barato basta.",
            TaskKind.DomScan => "Leitura de pagina e tarefa simples: modelo rapido.",
            TaskKind.Automation => "Automacao exige ferramentas: fiquei no equilibrio custo/capacidade.",
            TaskKind.Analysis => $"Analise longa pede capacidade e contexto ({model.ContextWindow:N0} tokens).",
            _ => "Escolha padrao do roteador."
        };
    }
}

/// <summary>Uma linha da tabela de modelos, ja formatada para a interface.</summary>
public sealed class ModelRow
{
    public ModelRow(ModelInfo model, bool hasKey, bool isPinned)
    {
        Id = model.Id;
        Name = model.DisplayName;
        Provider = model.Provider.ToString();
        HasKey = hasKey;
        IsPinned = isPinned;

        Tier = model.Tier switch
        {
            ModelTier.Fast => "Rapido",
            ModelTier.Balanced => "Equilibrado",
            ModelTier.Powerful => "Potente",
            _ => model.Tier.ToString()
        };

        Context = model.ContextWindow >= 1_000_000
            ? $"{model.ContextWindow / 1_000_000}M"
            : $"{model.ContextWindow / 1000}k";

        // Preco desconhecido aparece como travessao, e nao como "$0" — que faria
        // parecer gratuito.
        Cost = model.PricingKnown
            ? $"${model.CostInputPerMillion} / ${model.CostOutputPerMillion}"
            : "—";

        Tools = model.SupportsTools ? "Sim" : "Nao";

        Availability = hasKey ? string.Empty : "sem chave";
    }

    public string Id { get; }
    public string Name { get; }
    public string Provider { get; }
    public string Tier { get; }
    public string Context { get; }
    public string Cost { get; }
    public string Tools { get; }

    /// <summary>Ha chave cadastrada para o provedor deste modelo.</summary>
    public bool HasKey { get; }

    /// <summary>Este e o modelo fixado no momento.</summary>
    public bool IsPinned { get; }

    /// <summary>Vazio quando utilizavel; "sem chave" caso contrario.</summary>
    public string Availability { get; }

    public bool CanUse => HasKey && !IsPinned;
}
