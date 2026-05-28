namespace YukiVA.Orchestrator.Domain.ValueObjects;

public sealed record LlmResult(
    string Text,
    IReadOnlyList<ToolCallRequest> ToolCalls)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}
