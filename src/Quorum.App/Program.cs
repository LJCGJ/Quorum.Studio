using System;
using Avalonia;

namespace Quorum.App;

internal sealed class Program
{
    // Nada de Avalonia nem de codigo que dependa de SynchronizationContext antes
    // do Main: o framework ainda nao esta inicializado neste ponto.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Usada tambem pelo previewer de XAML do Visual Studio — nao remover.
    //
    // As ferramentas de diagnostico do Avalonia 11 sao anexadas na janela
    // (AttachDevTools em MainWindow), e nao aqui: WithDeveloperTools() so existe
    // no Avalonia 12, e o template gerou a versao de la.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
