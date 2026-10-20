using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Domain.Entities;

/// <summary>Диалоговая сессия — корень, через который добавляются сообщения.</summary>
public class ConversationSession
{
    private readonly List<Message> _messages = new();

    public long Id { get; private set; }
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; }

    // Сообщение нельзя добавить в обход методов Add*().
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();

    /// <summary>Вызовы последнего Assistant-сообщения, на которые ещё нет Tool-ответа.</summary>
    public IReadOnlyList<ToolCall> PendingToolCalls
    {
        get
        {
            var lastAssistant = _messages.FindLastIndex(m => m.Role == MessageRole.Assistant);
            if (lastAssistant < 0)
                return Array.Empty<ToolCall>();

            var answered = _messages
                .Skip(lastAssistant + 1)
                .Where(m => m.Role == MessageRole.Tool)
                .Select(m => m.ToolCallId)
                .ToHashSet();

            return _messages[lastAssistant].ToolCalls
                .Where(c => !answered.Contains(c.CallId))
                .ToList();
        }
    }

    public ConversationSession()
    {
        CreatedAt = DateTime.UtcNow;
    }

    public Message AddUserMessage(string text)
    {
        // Иначе LLM API вернёт 400 из-за «висящих» вызовов.
        EnsureNoPendingToolCalls();
        return Append(Message.User(text));
    }

    public Message AddAssistantMessage(string? text, IReadOnlyList<ToolCall> toolCalls)
    {
        EnsureNoPendingToolCalls();
        return Append(Message.Assistant(text, toolCalls));
    }

    public Message AddToolResult(string toolCallId, string content)
    {
        if (!PendingToolCalls.Any(c => c.CallId == toolCallId))
            throw new InvalidOperationException(
                $"Вызов инструмента '{toolCallId}' не ожидает ответа (неизвестный id или ответ уже есть).");

        return Append(Message.ToolResult(toolCallId, content));
    }

    private void EnsureNoPendingToolCalls()
    {
        if (PendingToolCalls.Count > 0)
            throw new InvalidOperationException(
                "Есть вызовы инструментов без результата — сначала добавьте их через AddToolResult.");
    }

    private Message Append(Message message)
    {
        _messages.Add(message);
        return message;
    }
}
