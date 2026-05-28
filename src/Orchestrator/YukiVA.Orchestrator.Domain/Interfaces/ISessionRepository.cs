using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Domain.Interfaces;

public interface ISessionRepository
{
    Task<AudioSession?> GetAsync(Guid sessionId, CancellationToken ct = default);
    Task<AudioSession> CreateAsync(string userId, CancellationToken ct = default);
    Task UpdateAsync(AudioSession session, CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
}
