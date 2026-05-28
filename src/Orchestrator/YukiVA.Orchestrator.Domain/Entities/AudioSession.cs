using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Domain.Entities;

public sealed class AudioSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public string UserId { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; private set; } = DateTimeOffset.UtcNow;
    public SessionState State { get; private set; } = SessionState.Active;

    private readonly List<ConversationMessage> _messages = [];
    public IReadOnlyList<ConversationMessage> Messages => _messages.AsReadOnly();

    private AudioSession(string userId) => UserId = userId;

    public static AudioSession Create(string userId) => new(userId);

    public void AddMessage(ConversationMessage message)
    {
        _messages.Add(message);
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void SetProcessing() => State = SessionState.Processing;
    public void SetActive() => State = SessionState.Active;

    public void Close()
    {
        State = SessionState.Closed;
        LastActivityAt = DateTimeOffset.UtcNow;
    }
}
