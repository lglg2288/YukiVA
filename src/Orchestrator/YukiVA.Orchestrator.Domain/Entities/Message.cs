using System;
using System.Collections.Generic;
using System.Text;
using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Domain.Entities;

/// <summary>
/// Одно сообщение в диалоге.
/// </summary>
public class Message
{
    public long Id { get; private set; }
    public Guid PublicId { get; private set; } = Guid.NewGuid();
    public long SessionId { get; private set; } //FOREIGN KEY to Session
    public MessageRole Role { get; private set; }
    public string Text { get; private set; }
    public DateTime CreatedAt { get; private set; }

    //The constructor only be called through a session. Whats why the internal
    internal Message(MessageRole role, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Текст сообщения не может быть пустым.", nameof(text));

        Role = role;
        Text = text;
        CreatedAt = DateTime.UtcNow;
    }
    //The constructor for EF Core
    private Message() { Text = null; }
}
