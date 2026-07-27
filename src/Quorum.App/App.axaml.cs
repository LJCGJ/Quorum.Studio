using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Quorum.App.ViewModels;
using Quorum.App.Views;

namespace Quorum.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();

            // Liga o alternador de tema do ViewModel ao tema da aplicacao.
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
                    RequestedThemeVariant = vm.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            };

            var janela = new MainWindow { DataContext = vm };
            vm.AttachWindow(janela);   // habilita os seletores de arquivo
            desktop.MainWindow = janela;

            // Fechar o app nao pode deixar navegadores e servidores MCP orfaos.
            desktop.ShutdownRequested += (_, _) => vm.ShutdownSessions();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
