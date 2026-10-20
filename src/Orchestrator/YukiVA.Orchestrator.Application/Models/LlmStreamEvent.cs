using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.Models;

/// <summary>Событие потокового ответа LLM.</summary>
public abstract record LlmStreamEvent;

/// <summary>Очередной кусок текста ответа.</summary>
public sealed record LlmTextDelta(string Text) : LlmStreamEvent;

/// <summary>Все вызовы инструментов ответа — приходит один раз, после текста.</summary>
public sealed record LlmToolCalls(IReadOnlyList<ToolCall> Calls) : LlmStreamEvent;

/// <summary>Конец ответа (stop, tool_calls, length…).</summary>
public sealed record LlmCompleted(string? FinishReason) : LlmStreamEvent;
