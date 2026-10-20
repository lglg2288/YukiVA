namespace YukiVA.Client.Wpf.Models;

/// <summary>
/// Одна реплика в переписке. Приходит из GET /api/sessions/{id}/messages.
/// </summary>
public sealed class ChatMessage
{
    public string Role { get; init; } = "";          // "User" или "Assistant"
    public string Text { get; init; } = "";
    public DateTimeOffset At { get; init; }

    /// <summary>Сообщение от пользователя? (используется в XAML, чтобы рисовать "пузырь" справа).</summary>
    public bool IsUser => string.Equals(Role, "User", StringComparison.OrdinalIgnoreCase);
}
