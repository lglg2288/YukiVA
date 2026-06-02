using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace YukiVA.Orchestrator.Infrastructure.Persistence;

public class OrchestratorDbContextFactory : IDesignTimeDbContextFactory<OrchestratorDbContext>
{
    public OrchestratorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=yukiva;Username=yukiva;Password=yukiva_dev_password")
            .Options;

        return new OrchestratorDbContext(options);
    }
}
