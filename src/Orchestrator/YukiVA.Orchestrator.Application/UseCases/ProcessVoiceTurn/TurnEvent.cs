using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

/// <summary>Событие хода — то, что API отдаёт клиенту по мере готовности.</summary>
public abstract record TurnEvent;

/// <summary>Всегда первое: в какой сессии идёт ход.</summary>
public sealed record SessionStarted(Guid SessionId) : TurnEvent;

/// <summary>Распознанная речь пользователя.</summary>
public sealed record UserTranscript(string Text) : TurnEvent;

/// <summary>Кусок текста ответа по мере генерации.</summary>
public sealed record AssistantTextDelta(string Text) : TurnEvent;

/// <summary>
/// Начинается озвучка предложения: все AudioChunk до следующего SentenceStarted — его звук.
/// По нему клиент подсвечивает в тексте то, что сейчас звучит. Index растёт в пределах ответа.
/// </summary>
public sealed record SentenceStarted(int Index, string Text) : TurnEvent;

/// <summary>Кусок озвучки в формате AudioFormat.Pcm16Mono24k; Seq растёт в пределах хода.</summary>
public sealed record AudioChunk(int Seq, ReadOnlyMemory<byte> Pcm) : TurnEvent;

/// <summary>Клиент должен исполнить ВСЕ вызовы и прислать результаты.</summary>
public sealed record ToolCallsRequested(IReadOnlyList<ToolCall> Calls) : TurnEvent;

/// <summary>Ошибка посреди потока (формирует API: HTTP-статус к этому моменту уже отправлен).</summary>
public sealed record TurnError(string Message) : TurnEvent;

/// <summary>Всегда последнее.</summary>
public sealed record TurnCompleted : TurnEvent;

/// <summary>Результат исполнения одного вызова инструмента клиентом.</summary>
public sealed record ToolResult(string ToolCallId, string Content);
