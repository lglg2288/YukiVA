using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Infrastructure.Mcp;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Repositories;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<SttOptions>(configuration.GetSection(SttOptions.Section));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.Section));
        services.Configure<TtsOptions>(configuration.GetSection(TtsOptions.Section));

        // Typed HTTP clients
        services.AddHttpClient<ISttService, SttHttpService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<SttOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = opts.Timeout;
            });

        services.AddHttpClient<ILlmService, LlmHttpService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = opts.Timeout;
            });

        services.AddHttpClient<ITtsService, TtsHttpService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<TtsOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = opts.Timeout;
            });

        // Repositories
        services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

        // MCP tool provider (gives tool definitions to LLM)
        services.AddScoped<IMcpToolProvider, McpToolProvider>();

        // MCP server tools (exposed to external MCP clients via /mcp endpoint)
        services.AddScoped<OrchestratorMcpServerTools>();

        return services;
    }
}
