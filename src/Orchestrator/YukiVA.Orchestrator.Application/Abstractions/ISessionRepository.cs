using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Хранилище диалоговых сессий.</summary>
public interface ISessionRepository
{
    /// <summary>Добавить новую сессию в контекст (ещё не сохраняет в БД).</summary>
    Task AddAsync(ConversationSession session, CancellationToken cancellationToken = default);

    /// <summary>Найти сессию по публичному идентификатору вместе с её сообщениями.</summary>
    Task<ConversationSession?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Зафиксировать накопленные изменения в БД.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
