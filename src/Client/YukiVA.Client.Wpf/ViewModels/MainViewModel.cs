using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YukiVA.Client.Core.Api;
using YukiVA.Client.Core.Audio;
using YukiVA.Client.Core.Conversation;
using YukiVA.Client.Wpf.Audio;
using YukiVA.Client.Wpf.Tools;

namespace YukiVA.Client.Wpf.ViewModels;

/// <summary>
/// Главный экран: зажал пробел — запись, отпустил — ход. Новый ход или Esc перебивают текущий.
/// Ход приходит из ConversationController потоком обновлений и раскладывается по ленте и панели Latency.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan MinSpeech = TimeSpan.FromMilliseconds(300);

    private readonly ConversationController _conversation;
    private readonly MicrophoneRecorder _recorder;
    private readonly IAudioOutput _audio;
    private readonly McpToolExecutor _tools;
    private readonly SpeechTimeline _timeline = new();
    private readonly Dictionary<int, AssistantMessageViewModel> _bubbles = new();
    private readonly Dictionary<string, ToolCallViewModel> _toolCalls = new();
    private readonly DispatcherTimer _playback;

    private CancellationTokenSource? _turn;
    private AssistantMessageViewModel? _highlighted;

    public MainViewModel(
        ConversationController conversation, MicrophoneRecorder recorder, IAudioOutput audio, McpToolExecutor tools)
    {
        _conversation = conversation;
        _recorder = recorder;
        _audio = audio;
        _tools = tools;

        _tools.StatusChanged += _ => App.Current.Dispatcher.Invoke(UpdateToolsStatus);
        UpdateToolsStatus();

        // Подсветка следует за звуком: позицию воспроизведения опрашиваем ~25 раз в секунду.
        _playback = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) => OnPlaybackTick(),
            App.Current.Dispatcher);
        _playback.Start();
    }

    public ObservableCollection<object> Items { get; } = new();
    public LatencyViewModel Latency { get; } = new();

    [ObservableProperty] private TurnStage _stage = TurnStage.Idle;
    [ObservableProperty] private string _toolsStatus = "";
    [ObservableProperty] private bool _toolsReady;

    private bool TurnRunning => _turn is not null;

    /// <summary>Запустить подключение к MCP заранее, чтобы первый ход не ждал docker run.</summary>
    public Task InitializeAsync() => _tools.EnsureConnectedAsync(CancellationToken.None);

    public void StartListening()
    {
        if (_recorder.IsRecording) return;

        Interrupt(); // заговорил поверх ответа — ответ останавливаем
        _recorder.Start();
        Stage = TurnStage.Listening;
    }

    public async Task StopListeningAndSendAsync()
    {
        if (!_recorder.IsRecording) return;

        var (wav, duration) = await _recorder.StopAsync();
        if (duration < MinSpeech)
        {
            Stage = TurnStage.Idle; // случайное нажатие
            return;
        }

        await RunTurnAsync(wav);
    }

    [RelayCommand]
    public void Interrupt()
    {
        _turn?.Cancel();
        _audio.Reset();
        ClearHighlight();
        if (!_recorder.IsRecording)
            Stage = TurnStage.Idle;
    }

    [RelayCommand]
    private void NewConversation()
    {
        Interrupt();
        _conversation.StartNewConversation();
        Items.Clear();
        Latency.BeginTurn();
    }

    private async Task RunTurnAsync(byte[] wav)
    {
        using var turn = new CancellationTokenSource();
        _turn = turn;
        _timeline.Clear();
        _bubbles.Clear();
        _toolCalls.Clear();
        Latency.BeginTurn();

        try
        {
            await foreach (var update in _conversation.RunTurnAsync(wav, turn.Token))
            {
                Apply(update);
                Latency.Apply(update);
            }
        }
        catch (OperationCanceledException) when (turn.IsCancellationRequested)
        {
            // Перебили — это нормально.
        }
        catch (Exception ex)
        {
            Items.Add(new ErrorMessageViewModel(Describe(ex)));
            Stage = TurnStage.Idle;
        }
        finally
        {
            if (_turn == turn) _turn = null;
        }

        if (Stage != TurnStage.Speaking && !_recorder.IsRecording)
            Stage = TurnStage.Idle;
    }

    private void Apply(TurnUpdate update)
    {
        switch (update)
        {
            case StageChanged s:
                Stage = s.Stage;
                break;

            case TranscriptReceived t:
                Items.Add(new UserMessageViewModel(string.IsNullOrWhiteSpace(t.Text) ? "(речь не распознана)" : t.Text));
                break;

            case AssistantMessageStarted m:
                var bubble = new AssistantMessageViewModel();
                _bubbles[m.Response] = bubble;
                Items.Add(bubble);
                break;

            case AssistantTextAppended a:
                _bubbles[a.Response].Append(a.Delta);
                break;

            case SentenceQueued s:
                _timeline.Add(s);
                break;

            case ToolCallStarted c:
                if (Items.LastOrDefault() is not ToolCallGroupViewModel group || group.Response != c.Response)
                {
                    group = new ToolCallGroupViewModel(c.Response);
                    Items.Add(group);
                }
                var card = new ToolCallViewModel(c.Call);
                _toolCalls[c.Call.Id] = card;
                group.Calls.Add(card);
                break;

            case ToolCallFinished f:
                _toolCalls[f.Call.Id].Complete(f.Result, f.Duration, f.Failed);
                break;

            case TurnFailed failed:
                Items.Add(new ErrorMessageViewModel(failed.Message));
                break;
        }
    }

    private void OnPlaybackTick()
    {
        var current = _timeline.Current(_audio.PlayedBytes, _audio.WrittenBytes);
        var bubble = current is { } c && _bubbles.TryGetValue(c.Response, out var b) ? b : null;

        if (bubble != _highlighted)
            ClearHighlight();
        if (bubble is not null && current is { } sentence)
        {
            bubble.Highlight(sentence.CharStart, sentence.CharLength);
            _highlighted = bubble;
        }

        // Ход закончился, звук доиграл — снова готовы слушать.
        if (Stage == TurnStage.Speaking && !TurnRunning && _audio.PlayedBytes >= _audio.WrittenBytes)
            Stage = TurnStage.Idle;
    }

    private void ClearHighlight()
    {
        _highlighted?.ClearHighlight();
        _highlighted = null;
    }

    private void UpdateToolsStatus()
    {
        ToolsReady = _tools.Status == McpStatus.Ready;
        ToolsStatus = _tools.Status switch
        {
            McpStatus.Ready => $"MCP: {_tools.Name}",
            McpStatus.Connecting => $"MCP: {_tools.Name} · подключение…",
            _ => $"MCP: {_tools.Name} недоступен"
        };
    }

    private static string Describe(Exception ex) => ex switch
    {
        VoiceApiException { StatusCode: HttpStatusCode.Unauthorized } =>
            "Оркестратор не принял API-ключ. Проверьте Api:Key в appsettings.json клиента.",
        VoiceApiException api => api.Message,
        HttpRequestException => "Оркестратор недоступен. Он запущен? Адрес — Api:BaseUrl в appsettings.json.",
        _ => ex.Message
    };
}
