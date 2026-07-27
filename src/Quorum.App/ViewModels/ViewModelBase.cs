using CommunityToolkit.Mvvm.ComponentModel;

namespace Quorum.App.ViewModels;

/// <summary>Base de todos os ViewModels (notificacao de mudanca via toolkit).</summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>Titulo exibido no cabecalho da pagina.</summary>
    public abstract string Title { get; }

    /// <summary>Uma linha explicando o que a pagina faz, na voz da interface.</summary>
    public abstract string Subtitle { get; }
}
