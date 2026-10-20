using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YukiVA.Orchestrator.Infrastructure;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Tests;

public class ConnectionWarmupServiceTests
{
    [Fact]
    public async Task Start_PingsFreeEndpoints_OfBothProviders()
    {
        var requests = await RunWarmupAsync(new()
        {
            ["Llm:ApiKey"] = "llm-key",
            ["Tts:ElevenLabs:ApiKey"] = "tts-key"
        }, expectedRequests: 2);

        Assert.Contains("GET https://api.deepseek.com/models", requests);
        Assert.Contains("GET https://api.elevenlabs.io/v1/models", requests);
    }

    [Fact]
    public async Task Start_XttsProvider_DoesNotTouchElevenLabs()
    {
        var requests = await RunWarmupAsync(new()
        {
            ["Llm:ApiKey"] = "llm-key",
            ["Tts:ElevenLabs:ApiKey"] = "tts-key",
            ["Tts:Provider"] = "Xtts"
        }, expectedRequests: 1);

        Assert.Equal(new[] { "GET https://api.deepseek.com/models" }, requests);
    }

    [Theory]
    [InlineData(false, "llm-key")] // выключено в конфиге
    [InlineData(true, "")]         // нет ключей — нечего греть
    public async Task Start_Disabled_SendsNothing(bool enabled, string key)
    {
        var requests = await RunWarmupAsync(new()
        {
            ["Warmup:Enabled"] = enabled.ToString(),
            ["Llm:ApiKey"] = key
        }, expectedRequests: 0);

        Assert.Empty(requests);
    }

    [Fact]
    public async Task KeepAlive_RepeatsPings()
    {
        var requests = await RunWarmupAsync(new()
        {
            ["Llm:ApiKey"] = "llm-key",
            ["Warmup:KeepAliveSeconds"] = "1"
        }, expectedRequests: 2);

        Assert.All(requests, r => Assert.Equal("GET https://api.deepseek.com/models", r));
    }

    [Fact]
    public async Task NetworkError_IsSwallowed()
    {
        var requests = await RunWarmupAsync(new()
        {
            ["Llm:ApiKey"] = "llm-key"
        }, expectedRequests: 1, fail: true);

        Assert.Single(requests); // сервис не упал, просто записал предупреждение
    }

    private static async Task<List<string>> RunWarmupAsync(
        Dictionary<string, string?> settings, int expectedRequests, bool fail = false)
    {
        settings.TryAdd("ConnectionStrings:Postgres", "Host=localhost;Database=unused");
        settings.TryAdd("Warmup:KeepAliveSeconds", "0");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var handler = new RecordingHandler(expectedRequests, fail);
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddInfrastructure(configuration);
        services.AddHttpClient(HttpClientNames.DeepSeek).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(HttpClientNames.ElevenLabs).ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var warmup = provider.GetServices<IHostedService>().OfType<ConnectionWarmupService>().Single();

        await warmup.StartAsync(CancellationToken.None);
        if (expectedRequests > 0)
            await handler.Done.WaitAsync(TimeSpan.FromSeconds(5));
        else
            await Task.Delay(200);
        await warmup.StopAsync(CancellationToken.None);

        return handler.Requests.ToList();
    }

    private sealed class RecordingHandler(int expected, bool fail) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<string> Requests { get; } = new();
        public Task Done => _done.Task;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Enqueue($"{request.Method} {request.RequestUri}");
            if (Requests.Count >= expected) _done.TrySetResult();

            return fail
                ? Task.FromException<HttpResponseMessage>(new HttpRequestException("сеть недоступна"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        }
    }
}
