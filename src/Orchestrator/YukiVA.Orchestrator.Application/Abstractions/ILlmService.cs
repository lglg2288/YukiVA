using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Языковая модель: по истории диалога возвращает ответ ассистента.</summary>
public interface ILlmService
{
    Task<string> CompleteAsync(
        IReadOnlyList<Message> history,
        CancellationToken cancellationToken = default);
}
