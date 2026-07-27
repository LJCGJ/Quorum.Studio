using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quorum.App.Services;

namespace Quorum.App.ViewModels;

/// <summary>
/// Shell da aplicacao: mantem as paginas, a navegacao lateral e o tema.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SessionService _session;

    public MainWindowViewModel()
    {
        _session = new SessionService();

        Chat = new ChatViewModel(_session);
        Automation = new AutomationViewModel(_session);
        Models = new ModelsViewModel(_session, OnPreferencesChanged);
        Settings = new SettingsViewModel(_session, OnPreferencesChanged);

        Pages = new List<ViewModelBase> { Chat, Automation, Models, Settings };
        _currentPage = Chat;
    }

    public ChatViewModel Chat { get; }
    public AutomationViewModel Automation { get; }
    public ModelsViewModel Models { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Paginas na ordem em que aparecem na navegacao.</summary>
    public List<ViewModelBase> Pages { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    /// <summary>Tema escuro e o padrao: a marca do Quorum nasceu sobre navy.</summary>
    [ObservableProperty]
    private bool _isDarkTheme = true;

    /// <summary>Liga os seletores de arquivo a janela principal.</summary>
    public void AttachWindow(Avalonia.Controls.Window janela) => _session.Files.Attach(janela);

    /// <summary>Interrompe tudo que estiver rodando ao fechar o aplicativo.</summary>
    public void ShutdownSessions() => _session.Sessions.CancelAll();

    [RelayCommand]
    private void Navigate(ViewModelBase page) => CurrentPage = page;

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    /// <summary>Configuracoes mudaram: as outras telas recalculam.</summary>
    private void OnPreferencesChanged()
    {
        // Uma mudanca de chave ou de modelo afeta as duas telas: o Chat mostra
        // quem responde, e a lista de Modelos marca qual esta em uso.
        Chat.UpdateRouting();
        Models.Recarregar();
    }
}
