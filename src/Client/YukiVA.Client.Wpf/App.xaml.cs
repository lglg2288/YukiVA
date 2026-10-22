using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YukiVA.Client.Core.Api;
using YukiVA.Client.Core.Audio;
using YukiVA.Client.Core.Conversation;
using YukiVA.Client.Wpf.Audio;
using YukiVA.Client.Wpf.Tools;
using YukiVA.Client.Wpf.ViewModels;

namespace YukiVA.Client.Wpf;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string Key { get; set; } = "";
    public string HeaderName { get; set; } = "X-Api-Key";
}

/// <summary>Composition root: конфиг, DI и время жизни сервисов — через Generic Host.</summary>
public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ContentRoot — папка exe: appsettings.json находится, откуда бы ни запускали.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration.AddEnvironmentVariables("YUKI_");

        builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
        builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));

        builder.Services.AddHttpClient<VoiceApiClient>((sp, http) =>
        {
            var api = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
            http.BaseAddress = new Uri(api.BaseUrl);
            http.Timeout = Timeout.InfiniteTimeSpan; // ответ — поток, его длину ограничивает отмена хода
            if (!string.IsNullOrWhiteSpace(api.Key))
                http.DefaultRequestHeaders.Add(api.HeaderName, api.Key);
        }).SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        builder.Services.AddSingleton<McpToolExecutor>();
        builder.Services.AddSingleton<NAudioOutput>();
        builder.Services.AddSingleton<IAudioOutput>(sp => sp.GetRequiredService<NAudioOutput>());
        builder.Services.AddSingleton<MicrophoneRecorder>();
        builder.Services.AddSingleton(sp => new ConversationController(
            sp.GetRequiredService<VoiceApiClient>(),
            sp.GetRequiredService<McpToolExecutor>(),
            sp.GetRequiredService<IAudioOutput>()));
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        _host.Start();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();

        // MCP-сервер стартует в фоне (docker run) — к первому вопросу инструменты уже готовы.
        _ = _host.Services.GetRequiredService<MainViewModel>().InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is { } host)
        {
            // Не в UI-потоке: асинхронная остановка, которой нужен Dispatcher, иначе повисла бы навсегда.
            // DisposeAsync, а не Dispose: MCP-клиент освобождается только асинхронно.
            var shutdown = Task.Run(async () =>
            {
                await host.StopAsync(TimeSpan.FromSeconds(3));
                await ((IAsyncDisposable)host).DisposeAsync();
            });
            shutdown.Wait(TimeSpan.FromSeconds(5));
        }
        base.OnExit(e);
    }
}
