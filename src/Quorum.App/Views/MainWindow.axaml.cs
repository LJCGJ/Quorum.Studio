using Avalonia;
using Avalonia.Controls;

namespace Quorum.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

#if DEBUG
        // Inspetor visual do Avalonia (F12 com o app rodando): mostra a arvore de
        // controles ao vivo, util para ajustar layout. So no Debug — o pacote
        // Avalonia.Diagnostics fica de fora do binario de Release.
        this.AttachDevTools();
#endif
    }
}
