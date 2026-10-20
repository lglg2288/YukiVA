using YukiVA.Orchestrator.Api.Endpoints;
using YukiVA.Orchestrator.Api.Security;
using YukiVA.Orchestrator.Application;
using Microsoft.EntityFrameworkCore;
using YukiVA.Orchestrator.Infrastructure;
using YukiVA.Orchestrator.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));

var app = builder.Build();

// Только недостающие миграции, данные не трогаются. Включено в Development.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<ApiKeyMiddleware>();
app.MapVoiceEndpoints();
app.Run();

// Для WebApplicationFactory в интеграционных тестах.
public partial class Program;
