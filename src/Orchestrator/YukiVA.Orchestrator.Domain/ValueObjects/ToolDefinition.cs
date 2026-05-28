namespace YukiVA.Orchestrator.Domain.ValueObjects;

public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputSchemaJson);
