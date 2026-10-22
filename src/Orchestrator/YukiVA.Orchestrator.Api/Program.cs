using YukiVA.Orchestrator.Api;
using YukiVA.Orchestrator.Api.Endpoints;
using YukiVA.Orchestrator.Api.Security;
using YukiVA.Orchestrator.Application;
using YukiVA.Orchestrator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));

var app = builder.Build();

// В Development: поднять базу (если не запущена) и применить миграции.
await DevDatabase.PrepareAsync(app);

app.UseMiddleware<ApiKeyMiddleware>();
app.MapVoiceEndpoints();
app.Run();

// Для WebApplicationFactory в интеграционных тестах.
public partial class Program;
