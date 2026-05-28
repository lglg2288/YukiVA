namespace YukiVA.Orchestrator.Domain.ValueObjects;

public sealed record ToolCallRequest(
    string CallId,
    string ToolName,
    string ArgumentsJson);
