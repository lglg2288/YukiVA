using YukiVA.Orchestrator.Api.Endpoints;
using YukiVA.Orchestrator.Api.Hubs;
using YukiVA.Orchestrator.Application;
using YukiVA.Orchestrator.Infrastructure;
using YukiVA.Orchestrator.Infrastructure.Mcp;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// Clean Architecture layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// MCP server — exposes orchestrator tools to MCP clients (client device, IDE, etc.)
// Endpoint: POST /mcp  (SSE transport is used by default)
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<OrchestratorMcpServerTools>();

// ── Pipeline ──────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Real-time audio streaming
app.MapHub<SpeechHub>("/hubs/speech");

// REST session management
app.MapSessionEndpoints();

// MCP endpoint for client-device MCP clients
app.MapMcp("/mcp");

app.Run();
