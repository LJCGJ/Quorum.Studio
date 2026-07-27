using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quorum.Agents;
using Quorum.App.Services;
using Quorum.Core.Ai;
using Quorum.Core.Models;
using Quorum.Providers;

namespace Quorum.App.ViewModels;

/// <summary>
/// Tela de conversa. Mostra, antes de enviar, QUAL modelo vai responder — a
/// decisao do roteador deixa de ser invisivel.
///
/// Enviar faz uma chamada real a IA e consome creditos da chave informada; a
/// interface diz isso com todas as letras antes de o usuario apertar o botao.
/// </summary>
public sealed partial class ChatViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private CancellationTokenSource? _cts;

    public ChatViewModel(SessionService session)
    {
        _session = session;
        Messages = new ObservableCollection<ChatEntry>();
        UpdateRouting();
    }

    public override string Title => "Chat";

    public override string Subtitle =>
        "Converse para planejar testes. O roteador escolhe quem responde.";

    public ObservableCollection<ChatEntry> Messages { get; }

    [ObservableProperty]
    private string _input = string.Empty;

    [ObservableProperty]
    private string _routingLabel = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Verdadeiro quando falta chave: a tela explica o que fazer.</summary>
    [ObservableProperty]
    private bool _needsApiKey;

    /// <summary>Atualiza o rotulo "quem vai responder" e o aviso de chave.</summary>
    public void UpdateRouting()
    {
        NeedsApiKey = !_session.HasApiKey;

        var result = _session.Router.Route(
            new TaskDescriptor(TaskKind.Chat), _session.Preferences);

        // Honestidade: sem chave nao ha modelo escolhido de verdade. Mostrar um
        // nome aqui daria a impressao de que o app ja esta pronto para responder.
        RoutingLabel = !_session.HasApiKey
            ? "Adicione uma chave para comecar"
            : result.Success
                ? $"Responde: {result.Selected!.DisplayName}"
                : "Nenhum modelo compativel";
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var pergunta = Input.Trim();
        if (pergunta.Length == 0) return;

        Messages.Add(ChatEntry.FromUser(pergunta));
        Input = string.Empty;

        var task = new TaskDescriptor(TaskKind.Chat);
        var chain = _session.Router.RouteWithFallback(task, _session.Preferences);
        if (chain.Count == 0)
        {
            Messages.Add(ChatEntry.FromSystem(
                "Nenhum modelo disponivel. Confira a chave de API em Configuracoes."));
            return;
        }

        IsBusy = true;
        StatusMessage = "Pensando...";
        _cts = new CancellationTokenSource();

        try
        {
            // A chave e escolhida a partir do modelo, e nao uma chave global:
            // com varias cadastradas, cada modelo usa a do seu provedor.
            var runner = new FallbackAgentRunner(
                modelId => _session.CreateProviderFor(modelId),
                AgentLoopOptions.Default,
                progresso => StatusMessage = progresso);

            var resultado = await runner.RunAsync(
                chain,
                SystemPrompt,
                pergunta,
                new NoToolsExecutor(),
                _cts.Token).ConfigureAwait(true);

            Messages.Add(ChatEntry.FromAssistant(resultado.FinalText, resultado));
        }
        catch (Exception ex)
        {
            // Bugs sobem ate aqui (o classificador ja separou o que e operacional):
            // mostrar o erro cru e melhor do que engoli-lo.
            Messages.Add(ChatEntry.FromSystem($"Erro inesperado: {ex.Message}"));
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanSend() => !IsBusy && _session.HasApiKey;

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        StatusMessage = string.Empty;
    }

    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    private const string SystemPrompt =
        "Voce e o Quorum, assistente especialista em automacao de testes, qualidade " +
        "de software (QA) e seguranca. Seja direto e pratico, como um engenheiro " +
        "senior. Ao gerar codigo, use blocos ```linguagem para o sistema conseguir " +
        "extrair e salvar.";

    /// <summary>
    /// Executor sem ferramentas: no modo Chat a IA so conversa. As ferramentas
    /// reais (navegador, banco, API) chegam com os agentes concretos.
    /// </summary>
    private sealed class NoToolsExecutor : IToolExecutor
    {
        public System.Collections.Generic.IReadOnlyList<ToolDefinition> Tools { get; } =
            Array.Empty<ToolDefinition>();

        public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("O modo Chat nao expoe ferramentas.");
    }
}

/// <summary>Uma linha da conversa, pronta para a interface.</summary>
public sealed class ChatEntry
{
    private ChatEntry(string author, string text, bool isUser, string meta)
    {
        Author = author;
        Text = text;
        IsUser = isUser;
        Meta = meta;
    }

    public string Author { get; }
    public string Text { get; }
    public bool IsUser { get; }

    /// <summary>Rodape com custo/estado, quando houver.</summary>
    public string Meta { get; }

    public bool HasMeta => Meta.Length > 0;

    public static ChatEntry FromUser(string text) => new("Voce", text, true, string.Empty);

    public static ChatEntry FromSystem(string text) => new("Quorum", text, false, string.Empty);

    public static ChatEntry FromAssistant(string text, AgentResult result)
    {
        var meta = result.TotalTokens is { } t ? $"{t:N0} tokens" : string.Empty;

        if (result.StopReason == AgentStopReason.Cancelled)
            meta = Join(meta, "interrompido");
        else if (result.StopReason == AgentStopReason.StepLimitReached)
            meta = Join(meta, "limite de passos atingido");
        else if (result.StopReason == AgentStopReason.Failed)
            meta = Join(meta, "falhou");

        if (result.OutputTruncated)
            meta = Join(meta, "resposta cortada no limite de tokens");

        return new ChatEntry("Quorum", text, false, meta);
    }

    private static string Join(string a, string b) =>
        a.Length == 0 ? b : $"{a}  ·  {b}";
}
