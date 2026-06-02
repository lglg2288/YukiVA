using System;
using System.Collections.Generic;
using System.Text;
using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Domain.Entities;

/// <summary>Диалоговая сессия — корень, через который добавляются сообщения.</summary>
public class ConversationSession
{
    private readonly List<Message> _messages = new();

    public long Id { get; private set; }
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; }

    // Сообщение нельзя добавить в обход AddMessage().
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();

    public ConversationSession()
    {
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Единственный способ добавить сообщение в диалог.</summary>
    public Message AddMessage(MessageRole role, string text)
    {
        var message = new Message(role, text); // правила проверяются внутри Message
        _messages.Add(message);
        return message;
    }
}
