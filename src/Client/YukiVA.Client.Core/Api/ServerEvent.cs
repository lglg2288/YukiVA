using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Core.Api;

/// <summary>Событие SSE-потока оркестратора (см. README, раздел API).</summary>
public abstract record ServerEvent;

public sealed record SessionEvent(Guid SessionId) : ServerEvent;

public sealed record AudioFormatEvent(int SampleRate, int Channels, int BitsPerSample) : ServerEvent;

public sealed record TranscriptEvent(string Text) : ServerEvent;

public sealed record TextEvent(string Delta) : ServerEvent;

/// <summary>Начинается озвучка предложения: следующие AudioEvent — его звук.</summary>
public sealed record SentenceEvent(int Index, string Text) : ServerEvent;

public sealed record AudioEvent(int Seq, byte[] Pcm) : ServerEvent;

public sealed record ToolCallsEvent(IReadOnlyList<ToolCall> Calls) : ServerEvent;

public sealed record ErrorEvent(string Message) : ServerEvent;

public sealed record DoneEvent : ServerEvent;
