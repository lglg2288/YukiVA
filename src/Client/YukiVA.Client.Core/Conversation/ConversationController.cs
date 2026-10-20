using System.Runtime.CompilerServices;
using System.Text;
using YukiVA.Client.Core.Api;
using YukiVA.Client.Core.Audio;
using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Core.Conversation;

/// <summary>
/// Один голосовой ход целиком: отправить речь, читать поток, звук — в вывод,
/// вызовы инструментов — исполнить и отправить результаты, пока модель не ответит текстом.
/// Интерфейс получает ход как поток <see cref="TurnUpdate"/>.
/// </summary>
public sealed class ConversationController
{
    /// <summary>Защита от зацикливания: модель раз за разом просит инструменты.</summary>
    public const int MaxToolRounds = 6;

    private readonly VoiceApiClient _api;
    private readonly IToolExecutor _tools;
    private readonly IAudioOutput _audio;
    private readonly TimeProvider _time;

    public ConversationController(VoiceApiClient api, IToolExecutor tools, IAudioOutput audio, TimeProvider? time = null)
    {
        _api = api;
        _tools = tools;
        _audio = audio;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Текущий диалог; null — следующий ход начнёт новый.</summary>
    public Guid? SessionId { get; private set; }

    public void StartNewConversation() => SessionId = null;

    public async IAsyncEnumerable<TurnUpdate> RunTurnAsync(byte[] wav, [EnumeratorCancellation] CancellationToken ct)
    {
        var started = _time.GetTimestamp();
        TimeSpan Now() => _time.GetElapsedTime(started);

        TimeSpan? transcriptAt = null, firstTextAt = null, firstAudioAt = null;
        var toolsTime = TimeSpan.Zero;

        _audio.Reset();
        yield return new StageChanged(Now(), TurnStage.Recognizing);

        var tools = await _tools.GetToolsAsync(ct);
        var stream = _api.SendVoiceAsync(wav, SessionId, tools, ct);

        for (var response = 1; ; response++)
        {
            var text = new StringBuilder();
            var searchFrom = 0;
            var audioInSentence = 0;
            IReadOnlyList<ToolCall>? calls = null;
            string? error = null;

            await foreach (var e in stream.WithCancellation(ct))
            {
                // Каждый кусок звука в лог не пишем — только первый кусок предложения.
                if (e is not AudioEvent || audioInSentence == 0)
                    yield return new ServerEventLogged(Now(), EventName(e), EventDetail(e));

                switch (e)
                {
                    case SessionEvent s:
                        SessionId = s.SessionId;
                        break;

                    case AudioFormatEvent f when f.SampleRate != IAudioOutput.SampleRate
                                              || f.Channels != IAudioOutput.Channels
                                              || f.BitsPerSample != IAudioOutput.BitsPerSample:
                        error = $"Неподдерживаемый формат звука: {f.SampleRate} Гц, {f.Channels} кан., {f.BitsPerSample} бит.";
                        break;

                    case TranscriptEvent t:
                        transcriptAt ??= Now();
                        yield return new TranscriptReceived(Now(), t.Text);
                        yield return new StageChanged(Now(), TurnStage.Thinking);
                        break;

                    case TextEvent d:
                        if (text.Length == 0)
                            yield return new AssistantMessageStarted(Now(), response);
                        firstTextAt ??= Now();
                        text.Append(d.Delta);
                        yield return new AssistantTextAppended(Now(), response, d.Delta);
                        break;

                    case SentenceEvent s:
                        // Сервер присылает предложение тем же текстом, что уже пришёл дельтами, — находим его в пузыре.
                        var start = text.ToString().IndexOf(s.Text, searchFrom, StringComparison.Ordinal);
                        if (start >= 0)
                        {
                            searchFrom = start + s.Text.Length;
                            yield return new SentenceQueued(Now(), response, start, s.Text.Length, _audio.WrittenBytes);
                        }
                        audioInSentence = 0;
                        break;

                    case AudioEvent a:
                        if (firstAudioAt is null)
                        {
                            firstAudioAt = Now();
                            yield return new StageChanged(Now(), TurnStage.Speaking);
                        }
                        audioInSentence++;
                        _audio.Write(a.Pcm);
                        break;

                    case ToolCallsEvent c:
                        calls = c.Calls;
                        break;

                    case ErrorEvent err:
                        error = err.Message;
                        break;
                }

                if (error is not null)
                    break;
            }

            if (error is not null)
            {
                yield return new TurnFailed(Now(), error);
                yield break;
            }

            if (calls is null || calls.Count == 0)
                break;

            if (response >= MaxToolRounds)
            {
                yield return new TurnFailed(Now(), $"Модель {MaxToolRounds} раз подряд запросила инструменты — ход остановлен.");
                yield break;
            }

            var results = new List<ToolResult>(calls.Count);
            foreach (var call in calls)
            {
                yield return new ToolCallStarted(Now(), response, call);

                var (result, duration, failed) = await ExecuteAsync(call, ct);
                toolsTime += duration;
                results.Add(new ToolResult(call.Id, result));

                yield return new ToolCallFinished(Now(), call, result, duration, failed);
            }

            stream = _api.SendToolResultsAsync(SessionId!.Value, results, tools, ct);
        }

        yield return new TurnFinished(Now(), new TurnTimings(transcriptAt, firstTextAt, firstAudioAt, Now(), toolsTime));
    }

    /// <summary>Ошибку инструмента отдаём модели текстом — пусть сама решит, как быть.</summary>
    private async Task<(string Result, TimeSpan Duration, bool Failed)> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        var started = _time.GetTimestamp();
        try
        {
            var result = await _tools.ExecuteAsync(call, ct);
            return (result, _time.GetElapsedTime(started), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ($"Ошибка инструмента: {ex.Message}", _time.GetElapsedTime(started), true);
        }
    }

    private static string EventName(ServerEvent e) => e switch
    {
        SessionEvent => "session",
        AudioFormatEvent => "audioFormat",
        TranscriptEvent => "transcript",
        TextEvent => "text",
        SentenceEvent => "sentence",
        AudioEvent => "audio",
        ToolCallsEvent => "toolCalls",
        ErrorEvent => "error",
        DoneEvent => "done",
        _ => e.GetType().Name
    };

    private static string? EventDetail(ServerEvent e) => e switch
    {
        TranscriptEvent t => t.Text,
        TextEvent d => d.Delta,
        SentenceEvent s => $"#{s.Index}",
        AudioEvent a => $"#{a.Seq}",
        ToolCallsEvent c => string.Join(", ", c.Calls.Select(x => x.Name)),
        ErrorEvent err => err.Message,
        _ => null
    };
}
