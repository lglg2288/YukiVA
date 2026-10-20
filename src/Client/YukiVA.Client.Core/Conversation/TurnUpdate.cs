using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Core.Conversation;

/// <summary>Этап хода — то, что показывает полоса статусов.</summary>
public enum TurnStage
{
    Idle,
    Listening,
    Recognizing,
    Thinking,
    Speaking
}

/// <summary>
/// Что произошло в ходе — для интерфейса. Elapsed отсчитывается от конца речи пользователя:
/// из этих отметок строится панель Latency.
/// </summary>
public abstract record TurnUpdate(TimeSpan Elapsed);

public sealed record StageChanged(TimeSpan Elapsed, TurnStage Stage) : TurnUpdate(Elapsed);

public sealed record TranscriptReceived(TimeSpan Elapsed, string Text) : TurnUpdate(Elapsed);

/// <summary>Новый пузырь ассистента. Response — номер ответа сервера в ходе (после tool calls — следующий).</summary>
public sealed record AssistantMessageStarted(TimeSpan Elapsed, int Response) : TurnUpdate(Elapsed);

public sealed record AssistantTextAppended(TimeSpan Elapsed, int Response, string Delta) : TurnUpdate(Elapsed);

/// <summary>
/// Предложение поставлено в очередь на озвучку: символы [CharStart, CharStart+CharLength) пузыря Response
/// звучат начиная с байта AudioStartByte общего звукового потока хода.
/// </summary>
public sealed record SentenceQueued(TimeSpan Elapsed, int Response, int CharStart, int CharLength, long AudioStartByte)
    : TurnUpdate(Elapsed);

public sealed record ToolCallStarted(TimeSpan Elapsed, int Response, ToolCall Call) : TurnUpdate(Elapsed);

public sealed record ToolCallFinished(TimeSpan Elapsed, ToolCall Call, string Result, TimeSpan Duration, bool Failed)
    : TurnUpdate(Elapsed);

/// <summary>Строка лога событий для панели Latency.</summary>
public sealed record ServerEventLogged(TimeSpan Elapsed, string Name, string? Detail) : TurnUpdate(Elapsed);

public sealed record TurnFailed(TimeSpan Elapsed, string Message) : TurnUpdate(Elapsed);

public sealed record TurnFinished(TimeSpan Elapsed, TurnTimings Timings) : TurnUpdate(Elapsed);

/// <summary>Ключевые задержки хода, от конца речи пользователя.</summary>
public sealed record TurnTimings(
    TimeSpan? Transcript,
    TimeSpan? FirstText,
    TimeSpan? FirstAudio,
    TimeSpan Total,
    TimeSpan Tools);
