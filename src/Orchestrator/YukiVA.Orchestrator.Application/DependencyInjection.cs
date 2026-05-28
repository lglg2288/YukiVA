using Microsoft.Extensions.DependencyInjection;
using YukiVA.Orchestrator.Application.UseCases.ProcessSpeechTurn;

namespace YukiVA.Orchestrator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProcessSpeechTurnHandler>();
        return services;
    }
}
