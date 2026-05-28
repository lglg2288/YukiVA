using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Infrastructure.Mcp;

/// <summary>
/// Provides tool definitions to the LLM and executes tool calls.
/// This is the internal side of MCP: connects to MCP servers registered for the user.
/// Extend this class (or replace with a dynamic registry) when adding DB/calendar tools.
/// </summary>
internal sealed class McpToolProvider : IMcpToolProvider
{
    // Tool registry — populated at startup; in the future sourced from user-specific MCP servers.
    private static readonly IReadOnlyList<ToolDefinition> BuiltInTools =
    [
        // Placeholder — will be filled once the first MCP server (user DB) is connected.
        // new ToolDefinition(
        //     "query_user_db",
        //     "Execute a read query against the user's personal database.",
        //     """{"type":"object","properties":{"sql":{"type":"string"}},"required":["sql"]}"""),
    ];

    private readonly Dictionary<string, Func<string, CancellationToken, Task<string>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ToolDefinition> GetAvailableTools() => BuiltInTools;

    public Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
    {
        if (_handlers.TryGetValue(toolName, out var handler))
            return handler(argumentsJson, ct);

        throw new NotSupportedException($"Tool '{toolName}' is not registered in this orchestrator.");
    }
}
