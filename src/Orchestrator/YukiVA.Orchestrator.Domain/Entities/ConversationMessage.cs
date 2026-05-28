using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Domain.Entities;

public sealed class ConversationMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid SessionId { get; }
    public MessageRole Role { get; }
    public string Content { get; }
    public string? ToolCallId { get; }
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    private ConversationMessage(Guid sessionId, MessageRole role, string content, string? toolCallId = null)
    {
        SessionId = sessionId;
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
    }

    public static ConversationMessage FromUser(Guid sessionId, string content) =>
        new(sessionId, MessageRole.User, content);

    public static ConversationMessage FromAssistant(Guid sessionId, string content) =>
        new(sessionId, MessageRole.Assistant, content);

    public static ConversationMessage FromSystem(Guid sessionId, string content) =>
        new(sessionId, MessageRole.System, content);

    public static ConversationMessage FromToolResult(Guid sessionId, string toolCallId, string content) =>
        new(sessionId, MessageRole.Tool, content, toolCallId);
}
