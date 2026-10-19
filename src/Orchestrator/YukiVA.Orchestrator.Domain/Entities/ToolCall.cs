namespace YukiVA.Orchestrator.Domain.Entities;

/// <summary>
/// Вызов инструмента, который запросила LLM. Value object, хранится внутри Message (jsonb).
/// </summary>
public sealed record ToolCall(string CallId, string Name, string ArgumentsJson);
