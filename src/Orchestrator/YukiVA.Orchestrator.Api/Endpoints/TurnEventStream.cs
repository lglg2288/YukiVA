using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

namespace YukiVA.Orchestrator.Api.Endpoints;

/// <summary>
/// События хода → SSE (event: имя, data: JSON в camelCase).
/// HTTP 200 уже отправлен к первому событию, поэтому ошибка внутри хода — событие error, затем done.
/// </summary>
public static class TurnEventStream
{
    public static async IAsyncEnumerable<SseItem<object>> ToSseAsync(
        IAsyncEnumerable<TurnEvent> events,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        var firstText = default(TimeSpan?);
        var firstAudio = default(TimeSpan?);

        await using var enumerator = events.GetAsyncEnumerator(ct);
        while (true)
        {
            TurnEvent current;
            string? error = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                current = enumerator.Current;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger.LogInformation("Клиент отключился, ход остановлен через {Elapsed} мс", timer.ElapsedMilliseconds);
                yield break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка посреди хода");
                error = ex is InvalidOperationException or InvalidDataException
                    ? ex.Message
                    : "Внутренняя ошибка сервера. Подробности — в логах оркестратора.";
                current = new TurnError(error);
            }

            switch (current)
            {
                case AssistantTextDelta: firstText ??= timer.Elapsed; break;
                case AudioChunk: firstAudio ??= timer.Elapsed; break;
            }

            yield return ToSseItem(current);

            if (current is SessionStarted)
                yield return AudioFormatItem;

            if (error is not null)
            {
                yield return ToSseItem(new TurnCompleted());
                yield break;
            }
        }

        logger.LogInformation(
            "Ход завершён: первый текст {FirstText} мс, первый звук {FirstAudio} мс, всего {Total} мс",
            firstText?.TotalMilliseconds, firstAudio?.TotalMilliseconds, timer.ElapsedMilliseconds);
    }

    private static readonly SseItem<object> AudioFormatItem = new(
        new
        {
            sampleRate = AudioFormat.Pcm16Mono24k.SampleRate,
            channels = AudioFormat.Pcm16Mono24k.Channels,
            bitsPerSample = AudioFormat.Pcm16Mono24k.BitsPerSample
        },
        "audioFormat");

    private static SseItem<object> ToSseItem(TurnEvent e) => e switch
    {
        SessionStarted s => new(new { sessionId = s.SessionId }, "session"),
        UserTranscript t => new(new { text = t.Text }, "transcript"),
        AssistantTextDelta d => new(new { delta = d.Text }, "text"),
        AudioChunk a => new(new { seq = a.Seq, pcm = Convert.ToBase64String(a.Pcm.Span) }, "audio"),
        ToolCallsRequested c => new(
            new { calls = c.Calls.Select(x => new { id = x.CallId, name = x.Name, arguments = x.ArgumentsJson }) },
            "toolCalls"),
        TurnError err => new(new { message = err.Message }, "error"),
        TurnCompleted => new(new { }, "done"),
        _ => throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "Неизвестное событие хода.")
    };
}
