using Microsoft.EntityFrameworkCore;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Infrastructure.Persistence;

namespace YukiVA.Orchestrator.Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly OrchestratorDbContext _db;

    public SessionRepository(OrchestratorDbContext context)
    {
        _db = context;
    }

    public async Task AddAsync(ConversationSession session, CancellationToken cancellationToken = default)
    {
        await _db.Sessions.AddAsync(session, cancellationToken);
    }

    public async Task<ConversationSession?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        return await _db.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.PublicId == publicId, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _db.SaveChangesAsync(cancellationToken);
    }
}
