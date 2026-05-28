using System.Collections.Concurrent;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Interfaces;

namespace YukiVA.Orchestrator.Infrastructure.Repositories;

/// <summary>
/// Thread-safe in-memory session store. Swap with a persistent implementation when needed.
/// </summary>
internal sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<Guid, AudioSession> _store = new();

    public Task<AudioSession?> GetAsync(Guid sessionId, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(sessionId, out var session) ? session : null);

    public Task<AudioSession> CreateAsync(string userId, CancellationToken ct = default)
    {
        var session = AudioSession.Create(userId);
        _store[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task UpdateAsync(AudioSession session, CancellationToken ct = default)
    {
        _store[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
