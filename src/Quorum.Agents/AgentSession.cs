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
        Status = resultado.StopReason switch
        {
            AgentStopReason.Completed => SessionStatus.Completed,
            AgentStopReason.Cancelled => SessionStatus.Cancelled,
            AgentStopReason.Failed => SessionStatus.Failed,
            AgentStopReason.StepLimitReached => SessionStatus.Completed,
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
