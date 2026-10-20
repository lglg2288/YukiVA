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
        AddTextToSpeech(services, configuration);
        services.AddScoped<ProcessVoiceTurnHandler>();


        return services;
    }

    // Оба TTS регистрируются под ключами (выбор в UI — Фаза 2),
    // а ITextToSpeech без ключа — тот, что указан в Tts:Provider.
    private static void AddTextToSpeech(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TtsOptions>(configuration.GetSection(TtsOptions.SectionName));

        services.AddHttpClient<ElevenLabsTtsService>((sp, client) =>
        {
            var elevenLabs = sp.GetRequiredService<IOptions<TtsOptions>>().Value.ElevenLabs;
            client.BaseAddress = new Uri(elevenLabs.BaseUrl);
            client.DefaultRequestHeaders.Add("xi-api-key", elevenLabs.ApiKey);
        });
        services.AddKeyedScoped<ITextToSpeech>(TtsOptions.ElevenLabsProvider,
            (sp, _) => sp.GetRequiredService<ElevenLabsTtsService>());
        services.AddKeyedScoped<ITextToSpeech, TtsGrpcClient>(TtsOptions.XttsProvider);

        var provider = configuration[$"{TtsOptions.SectionName}:{nameof(TtsOptions.Provider)}"]
            ?? TtsOptions.ElevenLabsProvider;
        if (provider is not (TtsOptions.ElevenLabsProvider or TtsOptions.XttsProvider))
            throw new InvalidOperationException(
                $"Неизвестный Tts:Provider '{provider}'. Допустимо: {TtsOptions.ElevenLabsProvider}, {TtsOptions.XttsProvider}.");

        services.AddScoped<ITextToSpeech>(sp => sp.GetRequiredKeyedService<ITextToSpeech>(provider));
    }
}
