using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;
using Microsoft.EntityFrameworkCore;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Infrastructure.Persistence;

public class OrchestratorDbContext : DbContext
{
    public OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options)
        : base(options) { }

    // DbSet — "таблица как коллекция объектов".
    public DbSet<ConversationSession> Sessions => Set<ConversationSession>();
    public DbSet<Message> Messages => Set<Message>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Подхватить все IEntityTypeConfiguration из этой сборки автоматически.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrchestratorDbContext).Assembly);
    }
}
