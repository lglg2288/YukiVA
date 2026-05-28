using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using YukiVA.Orchestrator.Domain.Interfaces;

namespace YukiVA.Orchestrator.Infrastructure.Mcp;

/// <summary>
/// Exposes orchestrator capabilities as MCP tools.
/// These are the tools available to MCP clients (client device, IDE, etc.)
/// via the /mcp HTTP endpoint.
/// </summary>
[McpServerToolType]
public sealed class OrchestratorMcpServerTools(ISessionRepository sessionRepository)
{
    [McpServerTool(Name = "get_conversation_history")]
    [Description("Returns the conversation history for an active session as JSON.")]
    public async Task<string> GetConversationHistory(
        [Description("Session ID (GUID)")] string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var guid))
            return "[]";

        var session = await sessionRepository.GetAsync(guid);
        if (session is null)
            return "[]";

        var messages = session.Messages.Select(m => new
        {
            role = m.Role.ToString().ToLowerInvariant(),
            content = m.Content,
            timestamp = m.Timestamp
        });

        return JsonSerializer.Serialize(messages);
    }

    [McpServerTool(Name = "get_session_state")]
    [Description("Returns the current state and metadata of a session.")]
    public async Task<string> GetSessionState(
        [Description("Session ID (GUID)")] string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var guid))
            return """{"error":"invalid_session_id"}""";

        var session = await sessionRepository.GetAsync(guid);
        if (session is null)
            return """{"error":"session_not_found"}""";

        return JsonSerializer.Serialize(new
        {
            id = session.Id,
            userId = session.UserId,
            state = session.State.ToString(),
            createdAt = session.CreatedAt,
            lastActivityAt = session.LastActivityAt,
            messageCount = session.Messages.Count
        });
    }
}
