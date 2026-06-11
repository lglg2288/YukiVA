using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Infrastructure.Persistence;
using YukiVA.Orchestrator.Infrastructure.Repositories;

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
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<ISessionRepository, SessionRepository>();
        return services;
    }
}
