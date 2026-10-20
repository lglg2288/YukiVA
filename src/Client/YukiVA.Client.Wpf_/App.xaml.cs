using System.Net.Http;
using System.Windows;
using YukiVA.Client.Wpf.Services;
using YukiVA.Client.Wpf.ViewModels;

namespace YukiVA.Client.Wpf;

/// <summary>
/// "Composition root" — единственное место, где создаются и связываются все части
/// приложения (HTTP-клиент, сервисы, ViewModel, окно). Здесь же удобно менять адрес сервера.
/// </summary>
public partial class App : Application
{
    private const string DefaultApiBaseUrl = "http://localhost:5000";

    private IToolProvider? _tools;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. HTTP-клиент к API оркестратора.
        var apiBaseUrl =
            Environment.GetEnvironmentVariable("YUKIVA_API_URL")
            ?? DefaultApiBaseUrl;
        var apiKey = Environment.GetEnvironmentVariable("YUKIVA_API_KEY");

        var http = new HttpClient
        {
            BaseAddress = new Uri(apiBaseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        // 2. Сервисы.
        var api = new VoiceApiClient(http);
        var recorder = new AudioRecorder();
        var player = new AudioPlayer();
        _tools = new McpToolProvider();

        // 3. ViewModel + окно. ViewModel становится "источником данных" для разметки.
        var viewModel = new MainViewModel(api, recorder, player, _tools);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_tools is not null) await _tools.DisposeAsync();
        base.OnExit(e);
    }
}
