using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Domain.Interfaces;

/// <summary>
/// Provides MCP tool definitions to the LLM and executes tool calls on its behalf.
/// Future implementations will connect to user-specific MCP servers (DB, calendar, etc.).
/// </summary>
public interface IMcpToolProvider
{
    IReadOnlyList<ToolDefinition> GetAvailableTools();
    Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default);
}
