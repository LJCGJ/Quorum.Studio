using Quorum.Core.Models;

namespace Quorum.Agents;

/// <summary>Estado de uma sessao de trabalho.</summary>
public enum SessionStatus
{
    /// <summary>Criada, ainda nao iniciada.</summary>
    Pending,

    /// <summary>Preparando o agente (subindo servidor MCP, por exemplo).</summary>
    Starting,

    /// <summary>Executando: a IA esta trabalhando.</summary>
    Running,

    /// <summary>Terminou e entregou um relatorio.</summary>
    Completed,

    /// <summary>
    /// Parou no teto de passos antes de concluir. Entregou o que conseguiu, mas
    /// NAO e o mesmo que concluir: merece estado proprio para o usuario ver isso
    /// sem precisar abrir o relatorio.
    /// </summary>
    StepLimitReached,

    /// <summary>Interrompida pelo usuario.</summary>
    Cancelled,

    /// <summary>Falhou; <see cref="AgentSession.FinalText"/> explica o motivo.</summary>
    Failed
}

/// <summary>
/// Uma tarefa em andamento: um agente, um objetivo e o modelo que o atende.
///
/// Sessoes sao INDEPENDENTES entre si e podem rodar ao mesmo tempo — enquanto uma
/// consulta um banco via MCP, outra pode conversar com o usuario ou revisar um
/// resultado anterior. Cada uma tem seu proprio cancelamento, progresso e custo.
/// </summary>
public sealed class AgentSession
{
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _progresso = new();
    private readonly object _gate = new();

    internal AgentSession(string title, string objective)
    {
        Id = Guid.NewGuid();
        Title = title;
        Objective = objective;
        StartedAt = DateTimeOffset.Now;
    }

    public Guid Id { get; }

    /// <summary>Nome curto para a interface (ex.: "Banco de Dados").</summary>
    public string Title { get; }

    /// <summary>O que o usuario pediu.</summary>
    public string Objective { get; }

    public DateTimeOffset StartedAt { get; }

    public SessionStatus Status { get; private set; } = SessionStatus.Pending;

    /// <summary>Modelo que atendeu (definido quando a execucao comeca).</summary>
    public string? ModelId { get; private set; }

    /// <summary>Relatorio final, ou a explicacao da falha.</summary>
    public string FinalText { get; private set; } = string.Empty;

    /// <summary>Tokens consumidos, quando o provedor informou.</summary>
    public long? TotalTokens { get; private set; }

    /// <summary>Passos usados no loop agentic.</summary>
    public int StepsUsed { get; private set; }

    /// <summary>Ultimas linhas de progresso (limitadas, para nao crescer sem fim).</summary>
    public IReadOnlyList<string> Progress
    {
        get { lock (_gate) return _progresso.ToArray(); }
    }

    // ---------------------------------------------------------------- revisao
    // A revisao e OPCIONAL e sob demanda: o usuario ve o resultado primeiro e so
    // entao decide se vale pagar por uma segunda opiniao.

    /// <summary>Estado da revisao desta tarefa.</summary>
    public SessionStatus? ReviewStatus { get; private set; }

    /// <summary>Texto da revisao, quando concluida.</summary>
    public string ReviewText { get; private set; } = string.Empty;

    /// <summary>Modelo que revisou.</summary>
    public string? ReviewModelId { get; private set; }

    /// <summary>Tokens gastos na revisao, contabilizados a parte do teste.</summary>
    public long? ReviewTokens { get; private set; }

    /// <summary>Se o revisor veio de outro provedor (opiniao independente).</summary>
    public bool ReviewIsIndependent { get; private set; }

    /// <summary>Ja existe revisao concluida ou em andamento.</summary>
    public bool HasReview => ReviewStatus is not null;

    internal void BeginReview(string modelId, bool independent)
    {
        ReviewModelId = modelId;
        ReviewIsIndependent = independent;
        ReviewStatus = SessionStatus.Running;
        Notify();
    }

    internal void CompleteReview(AgentResult resultado)
    {
        ReviewText = resultado.FinalText;
        ReviewTokens = resultado.TotalTokens;
        if (resultado.ModelId is { } m) ReviewModelId = m;

        ReviewStatus = resultado.StopReason switch
        {
            AgentStopReason.Cancelled => SessionStatus.Cancelled,
            AgentStopReason.Failed => SessionStatus.Failed,
            _ => SessionStatus.Completed
        };
        Notify();
    }

    internal void FailReview(string motivo)
    {
        ReviewText = motivo;
        ReviewStatus = SessionStatus.Failed;
        Notify();
    }

    /// <summary>Disparado a cada mudanca, para a interface se atualizar.</summary>
    public event Action<AgentSession>? Changed;

    /// <summary>True enquanto a sessao ocupa recursos (servidor MCP, chamadas a IA).</summary>
    public bool IsActive =>
        Status is SessionStatus.Starting or SessionStatus.Running;

    /// <summary>Pede o encerramento; a sessao termina como <see cref="SessionStatus.Cancelled"/>.</summary>
    public void Cancel()
    {
        if (!IsActive) return;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal CancellationToken Token => _cts.Token;

    internal void SetStatus(SessionStatus status)
    {
        Status = status;
        Notify();
    }

    internal void SetModel(string modelId)
    {
        ModelId = modelId;
        Notify();
    }

    internal void Report(string linha)
    {
        lock (_gate)
        {
            _progresso.Add(linha);
            // Progresso e para acompanhar o que esta acontecendo agora; guardar
            // tudo faria a memoria crescer numa automacao longa.
            if (_progresso.Count > 200) _progresso.RemoveAt(0);
        }
        Notify();
    }

    internal void Complete(AgentResult resultado)
    {
        FinalText = resultado.FinalText;
        TotalTokens = resultado.TotalTokens;
        StepsUsed = resultado.StepsUsed;
        // O modelo que efetivamente respondeu pode nao ser o escolhido no inicio,
        // se o fallback trocou de IA no meio.
        if (resultado.ModelId is { } modeloReal) ModelId = modeloReal;

        Status = resultado.StopReason switch
        {
            AgentStopReason.Completed => SessionStatus.Completed,
            AgentStopReason.Cancelled => SessionStatus.Cancelled,
            AgentStopReason.Failed => SessionStatus.Failed,
            AgentStopReason.StepLimitReached => SessionStatus.StepLimitReached,
            _ => SessionStatus.Completed
        };
        Notify();
    }

    internal void Fail(string motivo)
    {
        FinalText = motivo;
        Status = SessionStatus.Failed;
        Notify();
    }

    internal void Dispose() { try { _cts.Dispose(); } catch { /* ja descartado */ } }

    private void Notify() => Changed?.Invoke(this);
}
