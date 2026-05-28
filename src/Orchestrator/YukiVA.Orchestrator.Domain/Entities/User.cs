namespace YukiVA.Orchestrator.Domain.Entities;

public sealed class User
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Login { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
}
