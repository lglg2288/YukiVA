using Microsoft.Extensions.DependencyInjection;

namespace YukiVA.Orchestrator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<UseCases.ProcessVoiceTurn.ProcessVoiceTurnHandler>();
        
        return services;
    }
}
