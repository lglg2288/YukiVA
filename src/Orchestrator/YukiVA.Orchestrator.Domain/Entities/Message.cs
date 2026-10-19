using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Domain.Entities;

/// <summary>
/// Одно сообщение в диалоге.
/// </summary>
public class Message
{
    private readonly List<ToolCall> _toolCalls = new();

    public long Id { get; private set; }
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public long SessionId { get; private set; } //FOREIGN KEY to Session
    public MessageRole Role { get; private set; }

    // У Assistant с tool calls текст может быть пустым.
    public string? Text { get; private set; }

    // Только для Role == Tool: на какой вызов это ответ.
    public string? ToolCallId { get; private set; }

    // Только для Role == Assistant, вызовов может быть несколько.
    public IReadOnlyList<ToolCall> ToolCalls => _toolCalls.AsReadOnly();

    public DateTime CreatedAt { get; private set; }

    // Сообщения создаются только фабриками ниже, а те вызываются из ConversationSession.
    private Message(MessageRole role, string? text, string? toolCallId, IEnumerable<ToolCall>? toolCalls)
    {
        Role = role;
        Text = text;
        ToolCallId = toolCallId;
        if (toolCalls is not null)
            _toolCalls.AddRange(toolCalls);
        CreatedAt = DateTime.UtcNow;
    }
    //The constructor for EF Core
    private Message() { }

    internal static Message User(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Текст сообщения не может быть пустым.", nameof(text));

        return new Message(MessageRole.User, text, null, null);
    }

    internal static Message Assistant(string? text, IReadOnlyList<ToolCall> toolCalls)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        if (string.IsNullOrWhiteSpace(text) && toolCalls.Count == 0)
            throw new ArgumentException("У ответа ассистента должен быть текст или хотя бы один вызов инструмента.", nameof(text));

        foreach (var call in toolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.CallId))
                throw new ArgumentException("У вызова инструмента должен быть id.", nameof(toolCalls));
            if (string.IsNullOrWhiteSpace(call.Name))
                throw new ArgumentException("У вызова инструмента должно быть имя.", nameof(toolCalls));
        }

        if (toolCalls.Select(c => c.CallId).Distinct().Count() != toolCalls.Count)
            throw new ArgumentException("Id вызовов инструментов должны быть уникальными.", nameof(toolCalls));

        return new Message(MessageRole.Assistant, string.IsNullOrWhiteSpace(text) ? null : text, null, toolCalls);
    }

    internal static Message ToolResult(string toolCallId, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new Message(MessageRole.Tool, content, toolCallId, null);
    }
}
