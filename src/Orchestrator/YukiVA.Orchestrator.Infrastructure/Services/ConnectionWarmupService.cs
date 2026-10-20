using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Infrastructure.Options;

namespace YukiVA.Orchestrator.Infrastructure.Services;

/// <summary>
/// Заранее открывает HTTPS-соединения к DeepSeek и ElevenLabs и не даёт им закрыться по простою.
/// TLS-рукопожатие может занимать секунды — пусть это случится при старте, а не на первой фразе.
/// Запросы бесплатные: списки моделей, без генерации текста и синтеза.
/// </summary>
public sealed class ConnectionWarmupService : BackgroundService
{
    private readonly IHttpClientFactory _http;
    private readonly WarmupOptions _options;
    private readonly LlmOptions _llm;
    private readonly TtsOptions _tts;
    private readonly ILogger<ConnectionWarmupService> _logger;

    public ConnectionWarmupService(
        IHttpClientFactory http,
        IOptions<WarmupOptions> options,
        IOptions<LlmOptions> llm,
        IOptions<TtsOptions> tts,
        ILogger<ConnectionWarmupService> logger)
    {
        _http = http;
        _options = options.Value;
        _llm = llm.Value;
        _tts = tts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var targets = Targets().ToList();
        if (!_options.Enabled || targets.Count == 0)
            return;

        await PingAllAsync(targets, firstTime: true, stoppingToken);
        if (_options.KeepAliveSeconds <= 0)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.KeepAliveSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PingAllAsync(targets, firstTime: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private IEnumerable<(string Client, string Path)> Targets()
    {
        if (!string.IsNullOrWhiteSpace(_llm.ApiKey))
            yield return (HttpClientNames.DeepSeek, "/models");

        if (_tts.Provider == TtsOptions.ElevenLabsProvider && !string.IsNullOrWhiteSpace(_tts.ElevenLabs.ApiKey))
            yield return (HttpClientNames.ElevenLabs, "/v1/models");
    }

    private Task PingAllAsync(IEnumerable<(string Client, string Path)> targets, bool firstTime, CancellationToken ct) =>
        Task.WhenAll(targets.Select(t => PingAsync(t.Client, t.Path, firstTime, ct)));

    private async Task PingAsync(string clientName, string path, bool firstTime, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            // Статус не важен (даже 401 оставляет соединение в пуле) — важно дочитать ответ.
            using var response = await _http.CreateClient(clientName).GetAsync(path, ct);
            await response.Content.CopyToAsync(Stream.Null, ct);

            _logger.Log(firstTime ? LogLevel.Information : LogLevel.Debug,
                "Соединение с {Client} прогрето за {Elapsed} мс (HTTP {Status})",
                clientName, timer.ElapsedMilliseconds, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Прогрев — оптимизация: сеть недоступна сейчас — первый ход просто будет медленнее.
            _logger.LogWarning(ex, "Не удалось прогреть соединение с {Client}", clientName);
        }
    }
}
