using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quorum.App.Services;
using Quorum.Core.Models;
using Quorum.Core.Routing;

namespace Quorum.App.ViewModels;

/// <summary>
/// Chaves de API e preferencias que mudam como o Quorum escolhe e gasta.
/// Tudo aqui vale imediatamente nas outras telas.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly Action _onChanged;

    public SettingsViewModel(SessionService session, Action onChanged)
    {
        _session = session;
        _onChanged = onChanged;
        _economyMode = session.Preferences.EconomyMode;
        _rememberKeys = session.RememberKeys;

    }

    public override string Title => "Configuracoes";

    public override string Subtitle =>
        "Chaves de API e controle de custo. A escolha de modelo fica em Modelos.";

    /// <summary>Chaves cadastradas — uma por provedor.</summary>
    public ObservableCollection<ApiKeyEntry> Keys => _session.Keys;

    [ObservableProperty] private string _newKey = string.Empty;
    [ObservableProperty] private bool _economyMode;
    [ObservableProperty] private string _providerLabel = string.Empty;
    [ObservableProperty] private bool _hasProviderLabel;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _rememberKeys;

    /// <summary>
    /// Explica em que condicoes as chaves ficam guardadas. O texto muda por
    /// sistema porque a protecao muda de verdade — dizer o mesmo nos dois seria
    /// prometer no Linux uma seguranca que so o Windows oferece.
    /// </summary>
    public string VaultExplanation => _session.VaultIsEncrypted
        ? $"Guardadas cifradas pela sua conta do Windows, em {_session.VaultLocation}. " +
          "Outra conta no mesmo computador nao consegue le-las."
        : $"Guardadas em {_session.VaultLocation}, protegidas apenas pelas permissoes do " +
          "arquivo (so o seu usuario le). Nao ha cifragem neste sistema.";

    partial void OnRememberKeysChanged(bool value)
    {
        _session.RememberKeys = value;
        Message = value
            ? "As chaves ficarao guardadas para a proxima vez."
            : "Chaves guardadas apagadas deste computador.";
    }

    public bool HasKeys => Keys.Count > 0;

    partial void OnNewKeyChanged(string value)
    {
        // Enquanto digita, ja mostra a qual IA a chave pertence.
        if (string.IsNullOrWhiteSpace(value))
        {
            HasProviderLabel = false;
            return;
        }

        ProviderLabel = $"Reconhecida como {ModelRegistry.DetectProvider(value)}";
        HasProviderLabel = true;
    }

    partial void OnEconomyModeChanged(bool value) => Aplicar();

    [RelayCommand]
    private void AddKey()
    {
        if (string.IsNullOrWhiteSpace(NewKey))
        {
            Message = "Cole uma chave antes de adicionar.";
            return;
        }

        var entrada = _session.AddKey(NewKey);
        NewKey = string.Empty;
        HasProviderLabel = false;
        Message = $"Chave de {entrada.Provider} cadastrada.";

        OnPropertyChanged(nameof(HasKeys));
        Aplicar();
    }

    [RelayCommand]
    private void RemoveKey(ApiKeyEntry? entrada)
    {
        if (entrada is null) return;

        _session.RemoveKey(entrada);
        Message = $"Chave de {entrada.Provider} removida.";

        OnPropertyChanged(nameof(HasKeys));
        Aplicar();
    }

    private void Aplicar()
    {
        // A fixacao de modelo vive na tela Modelos; aqui so mexemos no custo,
        // entao ela e preservada.
        _session.Preferences = _session.Preferences with { EconomyMode = EconomyMode };

        _onChanged();
    }
}
