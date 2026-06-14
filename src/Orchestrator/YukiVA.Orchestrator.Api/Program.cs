using YukiVA.Orchestrator.Api.Endpoints;
using YukiVA.Orchestrator.Application;
using YukiVA.Orchestrator.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.MapVoiceEndpoints();
app.Run();
