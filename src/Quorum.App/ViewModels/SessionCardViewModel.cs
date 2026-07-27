using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quorum.Agents;
using Quorum.App.Services;
using Quorum.Core.Output;
using Quorum.Core.Routing;

namespace Quorum.App.ViewModels;

/// <summary>
/// Um cartao de tarefa na tela de Automacao: espelha uma <see cref="AgentSession"/>
/// e a mantem atualizada na interface.
///
/// A sessao dispara mudancas de uma thread de fundo; aqui elas sao levadas para a
/// thread da interface antes de tocar em qualquer propriedade observavel — sem
/// isso, o Avalonia lanca ao atualizar a tela fora da thread correta.
/// </summary>
public sealed partial class SessionCardViewModel : ObservableObject
{
    private readonly AgentSession _session;

    private readonly FileSaveService _files;
    private readonly SessionService? _servico;

    private readonly bool _autoReview;
    private bool _autoReviewDisparada;

    public SessionCardViewModel(AgentSession session, FileSaveService files,
        SessionService? servico = null, bool autoReview = false)
    {
        _session = session;
        _files = files;
        _servico = servico;
        _autoReview = autoReview;
        Progress = new ObservableCollection<string>();
        _session.Changed += AoMudar;
        Atualizar();
    }

    public Guid Id => _session.Id;

    public string Title => _session.Title;

    public string Objective => _session.Objective;

    /// <summary>Ultimas linhas de progresso, da mais recente para a mais antiga.</summary>
    public ObservableCollection<string> Progress { get; }

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private IBrush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _modelText = string.Empty;
    [ObservableProperty] private string _costText = string.Empty;
    [ObservableProperty] private string _elapsedText = string.Empty;
    [ObservableProperty] private string _finalText = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _hasProgress;

    /// <summary>Ha script extraivel no relatorio (a IA devolveu bloco de codigo).</summary>
    [ObservableProperty] private bool _hasScript;

    /// <summary>Descricao do script encontrado, ex.: "python · 24 linhas".</summary>
    [ObservableProperty] private string _scriptLabel = string.Empty;

    [ObservableProperty] private string _saveMessage = string.Empty;

    // ---------------------------------------------------------------- revisao
    [ObservableProperty] private bool _canReview;
    [ObservableProperty] private bool _hasReview;
    [ObservableProperty] private bool _reviewRunning;
    [ObservableProperty] private string _reviewText = string.Empty;
    [ObservableProperty] private string _reviewHeader = string.Empty;

    /// <summary>
    /// Diz quem revisaria e quanto custa, ANTES de o usuario clicar. Uma revisao
    /// e uma chamada paga a mais: a decisao tem que ser informada.
    /// </summary>
    [ObservableProperty] private string _reviewHint = string.Empty;

    /// <summary>Interrompe esta tarefa sem afetar as outras.</summary>
    [RelayCommand]
    private void Stop() => _session.Cancel();

    /// <summary>
    /// Salva o script que a IA gerou. Pega o ULTIMO bloco de codigo do relatorio:
    /// a IA costuma mostrar trechos parciais antes de entregar a versao final.
    /// </summary>
    [RelayCommand]
    private async Task SaveScriptAsync()
    {
        var bloco = CodeBlockExtractor.Last(_session.FinalText);
        if (bloco is null)
        {
            SaveMessage = "Este relatorio nao traz script.";
            return;
        }

        var nome = FileSaveService.SugerirNome($"script_{_session.Title}", bloco.FileExtension);
        var caminho = await _files.SaveAsync(
            bloco.Code, nome, $"Script {bloco.DisplayLanguage}", bloco.FileExtension);

        SaveMessage = caminho is null ? string.Empty : $"Salvo em {caminho}";
    }

    /// <summary>
    /// Pede a uma SEGUNDA IA que revise criticamente este resultado.
    ///
    /// Sob demanda, nunca automatico: e uma chamada paga a mais, e so o usuario
    /// sabe se aquele resultado especifico merece a segunda opiniao.
    /// </summary>
    [RelayCommand]
    private void Review()
    {
        if (_servico is null) return;

        var revisores = ReviewRouting.SelectReviewers(
            _servico.Registry, _servico.Preferences, _session.ModelId);

        if (revisores.Count == 0)
        {
            SaveMessage = "Nao ha outro modelo disponivel para revisar.";
            return;
        }

        var independente = ReviewRouting.IsIndependent(
            _servico.Registry, _session.ModelId, revisores[0]);

        _servico.Sessions.StartReview(
            _session, revisores, id => _servico.CreateProviderFor(id), independente);
    }

    /// <summary>Exporta o relatorio completo como HTML, para arquivar ou compartilhar.</summary>
    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (string.IsNullOrWhiteSpace(_session.FinalText))
        {
            SaveMessage = "Ainda nao ha relatorio para exportar.";
            return;
        }

        var html = ReportBuilder.BuildHtml(new ReportData(
            Title: _session.Title,
            Subtitle: _session.Objective,
            Body: _session.FinalText,
            Model: _session.ModelId,
            Tokens: _session.TotalTokens,
            Operator: Environment.UserName));

        var nome = FileSaveService.SugerirNome($"relatorio_{_session.Title}", ".html");
        var caminho = await _files.SaveAsync(html, nome, "Pagina HTML", ".html");

        SaveMessage = caminho is null ? string.Empty : $"Salvo em {caminho}";
    }

    /// <summary>Atualiza o tempo decorrido (chamado por um timer na tela).</summary>
    public void TickElapsed()
    {
        if (!_session.IsActive) return;
        ElapsedText = Formatar(DateTimeOffset.Now - _session.StartedAt);
    }

    /// <summary>Solta o vinculo com a sessao quando o cartao sai da tela.</summary>
    public void Unhook() => _session.Changed -= AoMudar;

    private void AoMudar(AgentSession _)
    {
        if (Dispatcher.UIThread.CheckAccess()) Atualizar();
        else Dispatcher.UIThread.Post(Atualizar);
    }

    private void Atualizar()
    {
        IsActive = _session.IsActive;

        (StatusText, StatusBrush) = _session.Status switch
        {
            SessionStatus.Pending   => ("Na fila",     Pincel("#94A3B8")),
            SessionStatus.Starting  => ("Preparando",  Pincel("#38BDF8")),
            SessionStatus.Running   => ("Executando",  Pincel("#38BDF8")),
            SessionStatus.Completed => ("Concluida",   Pincel("#34D399")),
            SessionStatus.StepLimitReached => ("Parou no limite", Pincel("#FBBF24")),
            SessionStatus.Cancelled => ("Interrompida",Pincel("#FBBF24")),
            SessionStatus.Failed    => ("Falhou",      Pincel("#F87171")),
            _                       => ("—",           Pincel("#94A3B8"))
        };

        ModelText = _session.ModelId is { } m ? m : "aguardando roteamento";

        // Custo e esforco lado a lado: e o que o usuario precisa para decidir se
        // vale repetir a tarefa.
        var partes = new System.Collections.Generic.List<string>();
        if (_session.TotalTokens is { } t) partes.Add($"{t:N0} tokens");
        if (_session.StepsUsed > 0) partes.Add($"{_session.StepsUsed} passos");
        CostText = string.Join("  ·  ", partes);

        ElapsedText = Formatar(DateTimeOffset.Now - _session.StartedAt);

        FinalText = _session.FinalText;
        HasResult = !string.IsNullOrWhiteSpace(_session.FinalText);

        AtualizarRevisao();

        // Script disponivel: mostra o que sera salvo antes de o usuario clicar.
        var bloco = CodeBlockExtractor.Last(_session.FinalText);
        HasScript = bloco is not null;
        ScriptLabel = bloco is null
            ? string.Empty
            : $"{bloco.DisplayLanguage} · {bloco.LineCount} linhas";

        // Progresso invertido: o que acabou de acontecer fica no topo, visivel sem
        // precisar rolar durante uma automacao longa.
        var atual = _session.Progress;
        if (atual.Count != Progress.Count)
        {
            Progress.Clear();
            foreach (var linha in atual.Reverse()) Progress.Add(linha);
        }
        HasProgress = Progress.Count > 0;
    }

    /// <summary>Estado da revisao e a dica de custo mostrada antes do clique.</summary>
    private void AtualizarRevisao()
    {
        var concluida = _session.Status is SessionStatus.Completed or SessionStatus.StepLimitReached;
        HasReview = _session.HasReview;
        ReviewRunning = _session.ReviewStatus == SessionStatus.Running;

        // So faz sentido revisar o que terminou e produziu relatorio, e so uma vez.
        CanReview = concluida && !string.IsNullOrWhiteSpace(_session.FinalText)
                    && !HasReview && _servico is not null;

        if (CanReview && _servico is not null)
        {
            var revisores = ReviewRouting.SelectReviewers(
                _servico.Registry, _servico.Preferences, _session.ModelId);

            ReviewHint = revisores.Count == 0
                ? "Cadastre a chave de outro provedor para ter uma segunda opiniao."
                : ReviewRouting.IsIndependent(_servico.Registry, _session.ModelId, revisores[0])
                    ? $"Consome tokens: {revisores[0].DisplayName} le e critica este relatorio."
                    : $"Consome tokens: {revisores[0].DisplayName} revisa (mesmo provedor — " +
                      "opiniao menos independente).";
        }

        // O usuario pediu a revisao ja no disparo: assim que o resultado chega, ela
        // e acionada — uma unica vez, mesmo com varios eventos de atualizacao.
        if (_autoReview && CanReview && !_autoReviewDisparada)
        {
            _autoReviewDisparada = true;
            Review();
        }

        if (!HasReview) return;

        ReviewText = _session.ReviewText;
        var partes = new System.Collections.Generic.List<string>();
        if (_session.ReviewModelId is { } m) partes.Add(m);
        if (_session.ReviewTokens is { } t) partes.Add($"{t:N0} tokens");
        partes.Add(_session.ReviewIsIndependent ? "opiniao independente" : "mesmo provedor");

        ReviewHeader = _session.ReviewStatus switch
        {
            SessionStatus.Running => "Revisando...",
            SessionStatus.Failed => "Revisao falhou",
            _ => "Revisao  ·  " + string.Join("  ·  ", partes)
        };
    }

    private static IBrush Pincel(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static string Formatar(TimeSpan t) =>
        t.TotalMinutes < 1 ? $"{t.Seconds}s"
        : t.TotalHours < 1 ? $"{t.Minutes}min {t.Seconds}s"
        : $"{(int)t.TotalHours}h {t.Minutes}min";
}
