using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quorum.Agents;
using Quorum.App.Services;
using Quorum.Core.Models;
using Quorum.Mcp;
using Quorum.Providers;

namespace Quorum.App.ViewModels;

/// <summary>Tipos de automacao que o usuario pode disparar.</summary>
public enum AutomationKind
{
    Screen,
    Api,
    Database,
    Mongo
}

/// <summary>
/// Tipo de automacao com o nome que o usuario ve. O ComboBox exibe o ToString,
/// entao o rotulo fica em portugues sem template — antes aparecia o nome interno
/// do codigo ("Screen", "Api"), que nao diz nada a quem usa.
/// </summary>
public sealed record KindOption(AutomationKind Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Tela de Automacao: dispara tarefas e acompanha as que estao rodando.
///
/// Varias tarefas podem correr ao mesmo tempo — e opcao, nao obrigacao. Cada uma
/// tem seu modelo, seu progresso e seu botao de parar; e o Chat continua
/// disponivel enquanto elas trabalham.
/// </summary>
public sealed partial class AutomationViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly DispatcherTimer _relogio;

    public AutomationViewModel(SessionService session)
    {
        _session = session;
        Cards = new ObservableCollection<SessionCardViewModel>();
        _selectedKind = Kinds[0];

        _session.Sessions.SessionsChanged += AoMudarLista;

        // Atualiza o tempo decorrido dos cartoes ativos.
        _relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _relogio.Tick += (_, _) =>
        {
            foreach (var c in Cards) c.TickElapsed();
        };
        _relogio.Start();

        AtualizarAviso();
    }

    public override string Title => "Automacao";

    public override string Subtitle =>
        "Dispare testes reais e acompanhe cada tarefa. Varias podem rodar ao mesmo tempo.";

    public ObservableCollection<SessionCardViewModel> Cards { get; }

    public IReadOnlyList<KindOption> Kinds { get; } = new[]
    {
        new KindOption(AutomationKind.Screen, "Teste de tela (navegador)"),
        new KindOption(AutomationKind.Api, "Teste de API (HTTP)"),
        new KindOption(AutomationKind.Database, "Banco de dados (SQL)"),
        new KindOption(AutomationKind.Mongo, "MongoDB")
    };

    [ObservableProperty] private KindOption _selectedKind;

    /// <summary>
    /// Pedir a segunda IA ao concluir — o "duplo processamento". OPCIONAL e por
    /// tarefa: cada revisao e uma chamada paga a mais, entao a escolha e sua, aqui,
    /// antes de disparar. (Tambem da para pedir depois, no cartao da tarefa.)
    /// </summary>
    [ObservableProperty] private bool _reviewAfter;
    [ObservableProperty] private string _target = string.Empty;
    [ObservableProperty] private string _objective = string.Empty;
    [ObservableProperty] private bool _readOnly = true;
    [ObservableProperty] private bool _headless;
    [ObservableProperty] private string _notice = string.Empty;
    [ObservableProperty] private bool _hasNotice;

    /// <summary>Rotulo do campo de alvo, que muda conforme o tipo escolhido.</summary>
    public string TargetLabel => SelectedKind.Value switch
    {
        AutomationKind.Screen => "URL da pagina",
        AutomationKind.Api => "URL do endpoint",
        AutomationKind.Database => "String de conexao",
        AutomationKind.Mongo => "String de conexao",
        _ => "Alvo"
    };

    public string TargetHint => SelectedKind.Value switch
    {
        AutomationKind.Screen => "https://meusistema.com/login",
        AutomationKind.Api => "https://api.exemplo.com/itens",
        AutomationKind.Database => "postgres://usuario:senha@localhost:5432/banco",
        AutomationKind.Mongo => "mongodb://localhost:27017/banco",
        _ => ""
    };

    /// <summary>Opcoes que so fazem sentido em alguns tipos.</summary>
    public bool ShowReadOnly => SelectedKind.Value is AutomationKind.Database or AutomationKind.Mongo;
    public bool ShowHeadless => SelectedKind.Value is AutomationKind.Screen;

    /// <summary>Aviso de consumo, para o usuario decidir com clareza.</summary>
    public string CostWarning => SelectedKind.Value switch
    {
        AutomationKind.Screen =>
            "Abre um navegador real e consome mais tokens que as demais.",
        AutomationKind.Api =>
            "A mais leve: nao precisa de Node nem abre navegador.",
        _ => "Explora o schema antes de consultar; use um usuario com privilegios minimos."
    };

    public string ConcurrencyInfo =>
        $"Ate {_session.Sessions.MaxConcurrent} tarefas ao mesmo tempo. " +
        "As demais aguardam vaga — cada automacao de tela abre um navegador e usa memoria.";

    partial void OnSelectedKindChanged(KindOption value)
    {
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(TargetHint));
        OnPropertyChanged(nameof(ShowReadOnly));
        OnPropertyChanged(nameof(ShowHeadless));
        OnPropertyChanged(nameof(CostWarning));
    }

    [RelayCommand]
    private void Start()
    {
        if (!Validar()) return;

        var alvo = Target.Trim();
        var objetivo = Objective.Trim();
        var somenteLeitura = ReadOnly;
        var semJanela = Headless;
        var tipo = SelectedKind.Value;

        var tarefa = new TaskDescriptor(TaskKind.Automation);
        var cadeia = _session.Router.RouteWithFallback(tarefa, _session.Preferences);

        var sessao = _session.Sessions.Start(
            NomeDoTipo(tipo),
            objetivo,
            (log, ct) => CriarAgente(tipo, alvo, somenteLeitura, semJanela, log, ct),
            cadeia,
            modelId => _session.CreateProviderFor(modelId));

        Cards.Insert(0, new SessionCardViewModel(sessao, _session.Files, _session, autoReview: ReviewAfter));

        Objective = string.Empty;
        AtualizarAviso();
    }

    private bool Validar()
    {
        if (!_session.HasApiKey)
        {
            Mostrar("Informe uma chave de API em Configuracoes antes de disparar uma tarefa.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(Target))
        {
            Mostrar($"Preencha {TargetLabel.ToLowerInvariant()}.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(Objective))
        {
            Mostrar("Descreva o que voce quer testar.");
            return false;
        }

        HasNotice = false;
        return true;
    }

    private static Task<IQuorumAgent> CriarAgente(
        AutomationKind tipo, string alvo, bool somenteLeitura, bool semJanela,
        Action<string> log, CancellationToken ct) => tipo switch
    {
        AutomationKind.Screen =>
            ScreenAgent.CreateAsync(alvo, semJanela, log, ct).ContinueWith(
                t => (IQuorumAgent)t.Result, ct, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default),

        AutomationKind.Database =>
            DatabaseAgent.CreateRelationalAsync(alvo, somenteLeitura, log, ct).ContinueWith(
                t => (IQuorumAgent)t.Result, ct, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default),

        AutomationKind.Mongo =>
            DatabaseAgent.CreateMongoAsync(alvo, somenteLeitura, log, ct).ContinueWith(
                t => (IQuorumAgent)t.Result, ct, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default),

        _ => Task.FromResult<IQuorumAgent>(new ApiAgent($"URL base: {alvo}"))
    };

    private static string NomeDoTipo(AutomationKind k) => k switch
    {
        AutomationKind.Screen => "Teste de Tela",
        AutomationKind.Api => "Teste de API",
        AutomationKind.Database => "Banco de Dados",
        AutomationKind.Mongo => "MongoDB",
        _ => "Automacao"
    };

    /// <summary>Remove os cartoes ja encerrados, mantendo os ativos.</summary>
    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var card in Cards.Where(c => !c.IsActive).ToList())
        {
            _session.Sessions.Remove(card.Id);
            card.Unhook();
            Cards.Remove(card);
        }
        AtualizarAviso();
    }

    /// <summary>Interrompe tudo que estiver rodando.</summary>
    [RelayCommand]
    private void StopAll() => _session.Sessions.CancelAll();

    private void AoMudarLista() { /* a lista de cartoes e mantida localmente */ }

    private void AtualizarAviso()
    {
        if (!_session.HasApiKey)
            Mostrar("Informe uma chave de API em Configuracoes para disparar tarefas.");
        else
            HasNotice = false;
    }

    private void Mostrar(string mensagem)
    {
        Notice = mensagem;
        HasNotice = true;
    }
}
