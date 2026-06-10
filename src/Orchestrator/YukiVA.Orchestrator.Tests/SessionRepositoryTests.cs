using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;
using YukiVA.Orchestrator.Infrastructure.Persistence;
using YukiVA.Orchestrator.Infrastructure.Repositories;

namespace YukiVA.Orchestrator.Tests;

// IAsyncLifetime — xUnit вызовет InitializeAsync до тестов и DisposeAsync после.
public class SessionRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    // Поднять контейнер и накатить схему ДО тестов.
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync(); // применяем наши миграции к свежей базе
    }

    // Убить контейнер ПОСЛЕ тестов.
    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    // Каждый раз новый DbContext, подключённый к контейнеру.
    private OrchestratorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new OrchestratorDbContext(options);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSessionWithMessages()
    {
        // Arrange — создаём сессию с двумя сообщениями
        var session = new ConversationSession();
        session.AddMessage(MessageRole.User, "привет");
        session.AddMessage(MessageRole.Assistant, "здравствуй!");
        var publicId = session.PublicId;

        // Act — сохраняем через репозиторий
        await using (var db = CreateContext())
        {
            var repo = new SessionRepository(db);
            await repo.AddAsync(session);
            await repo.SaveChangesAsync();
        }

        // ...и читаем обратно НОВЫМ контекстом (чтобы данные точно пришли из БД,
        // а не остались в памяти первого контекста)
        await using (var db = CreateContext())
        {
            var repo = new SessionRepository(db);
            var loaded = await repo.GetByPublicIdAsync(publicId);

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Messages.Count);
            Assert.Equal(MessageRole.User, loaded.Messages[0].Role);
            Assert.Equal("привет", loaded.Messages[0].Text);
            Assert.True(loaded.Id > 0); // внутренний ключ присвоила БД
        }
    }
}
