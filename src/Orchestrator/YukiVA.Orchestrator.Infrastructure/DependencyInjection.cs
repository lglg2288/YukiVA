using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;
using YukiVA.Orchestrator.Infrastructure.Grpc.Stt;
using YukiVA.Orchestrator.Infrastructure.Grpc.Tts;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Persistence;
using YukiVA.Orchestrator.Infrastructure.Repositories;
using YukiVA.Orchestrator.Infrastructure.Grpc;
using YukiVA.Orchestrator.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace YukiVA.Orchestrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connecrionString = configuration.GetConnectionString("Postgres") ??
            throw new InvalidOperationException("Connection string 'Postgres' not set. (DI from Infrastructure)");

        services.AddDbContext<OrchestratorDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.Configure<ServiceEndpointsOptions>(configuration.GetSection(ServiceEndpointsOptions.SectionName));
        services.AddGrpcClient<STTservice.STTserviceClient>((sp, options) =>
        {
            var endpoints = sp.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;
            options.Address = new Uri(endpoints.Stt);
        });
        services.AddGrpcClient<TTSservice.TTSserviceClient>((sp, options) =>
        {
            var endpoints = sp.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;
            options.Address = new Uri(endpoints.Tts);
        });
        services.AddHttpClient<ILlmService, DeepSeekLlmService>((sp, client) =>
        {
            var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            client.BaseAddress = new Uri(llm.BaseUrl);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", llm.ApiKey);
        });
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ISpeechToText, SttGrpcClient>();
        services.AddScoped<ITextToSpeech, TtsGrpcClient>();
        services.AddScoped<ProcessVoiceTurnHandler>();


        return services;
    }
}
