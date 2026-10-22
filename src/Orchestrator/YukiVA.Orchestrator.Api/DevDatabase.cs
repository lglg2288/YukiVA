using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using YukiVA.Orchestrator.Infrastructure.Persistence;

namespace YukiVA.Orchestrator.Api;

/// <summary>
/// Только для разработки: база недоступна — поднимаем её из корневого docker-compose.yml и применяем миграции.
/// Так оркестратор запускается одной командой, без отдельного «docker compose up».
/// </summary>
public static class DevDatabase
{
    private static readonly TimeSpan ComposeTimeout = TimeSpan.FromMinutes(2);

    public static async Task PrepareAsync(WebApplication app)
    {
        var config = app.Configuration;
        if (!config.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
            return;

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevDatabase));

        if (!await db.Database.CanConnectAsync() && config.GetValue<bool>("Database:StartWithDockerCompose"))
            await StartWithComposeAsync(app.Environment.ContentRootPath, logger);

        // Только недостающие миграции, данные не трогаются.
        await db.Database.MigrateAsync();
    }

    private static async Task StartWithComposeAsync(string contentRoot, ILogger logger)
    {
        var composeFile = FindUpwards(contentRoot, "docker-compose.yml")
            ?? throw new InvalidOperationException("База недоступна, а корневой docker-compose.yml не найден.");

        logger.LogInformation("База недоступна — запускаю {File}", composeFile);
        var timer = Stopwatch.StartNew();

        // --wait: дождаться healthcheck, чтобы миграции не упали на ещё стартующем Postgres.
        using var docker = Process.Start(new ProcessStartInfo("docker")
        {
            ArgumentList = { "compose", "-f", composeFile, "up", "-d", "--wait" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Не удалось запустить docker.");

        using var timeout = new CancellationTokenSource(ComposeTimeout);
        var output = docker.StandardError.ReadToEndAsync(timeout.Token);
        await docker.WaitForExitAsync(timeout.Token);

        if (docker.ExitCode != 0)
            throw new InvalidOperationException(
                $"docker compose up завершился с кодом {docker.ExitCode}. Docker Desktop запущен?\n{await output}");

        logger.LogInformation("База запущена за {Elapsed} мс", timer.ElapsedMilliseconds);
    }

    private static string? FindUpwards(string start, string fileName)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
