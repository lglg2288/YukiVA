using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Tests;

/// <summary>
/// Прогон против настоящего DeepSeek. Ключ — из user-secrets проекта Api (Llm:ApiKey)
/// или переменной окружения Llm__ApiKey; без ключа тесты ничего не делают.
/// Запуск: dotnet test --filter Category=Manual --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "Manual")]
public class DeepSeekLlmServiceManualTests
{
    private const string ApiUserSecretsId = "f2ca71cb-aea5-43f0-b6a0-21320c95bac4";

    private static readonly ToolDefinition[] Tools =
    {
        new("get_time", "Текущее время в указанном городе.",
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}"""),
        new("get_weather", "Текущая погода в указанном городе.",
            """{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}""")
    };

    private readonly ITestOutputHelper _output;

    public DeepSeekLlmServiceManualTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RealApi_StreamsText()
    {
        var service = CreateService();
        if (service is null) return;

        var session = new ConversationSession();
        session.AddUserMessage("Расскажи в трёх предложениях, что такое SSE.");

        var events = await RunAsync(service, session, Array.Empty<ToolDefinition>());

        Assert.True(events.OfType<LlmTextDelta>().Count() > 1, "Ожидали несколько дельт — значит, поток работает.");
        Assert.Equal("stop", Assert.IsType<LlmCompleted>(events[^1]).FinishReason);
    }

    [Fact]
    public async Task RealApi_TwoToolCalls_ThenAnswerWithResults()
    {
        var service = CreateService();
        if (service is null) return;

        var session = new ConversationSession();
        session.AddUserMessage("Сколько сейчас времени в Токио и какая там погода? Вызови оба инструмента.");

        var first = await RunAsync(service, session, Tools);
        var calls = Assert.IsType<LlmToolCalls>(first.Single(e => e is LlmToolCalls)).Calls;
        Assert.True(calls.Count >= 1);

        session.AddAssistantMessage(
            string.Concat(first.OfType<LlmTextDelta>().Select(d => d.Text)), calls);
        foreach (var call in calls)
        {
            session.AddToolResult(call.CallId, call.Name == "get_time"
                ? """{"time":"21:15"}"""
                : """{"weather":"ясно, +18"}""");
        }

        // Если история с tool_calls/tool_call_id собрана неверно, API вернёт 400.
        var second = await RunAsync(service, session, Tools);

        Assert.NotEmpty(second.OfType<LlmTextDelta>());
        Assert.Equal("stop", Assert.IsType<LlmCompleted>(second[^1]).FinishReason);
    }

    private async Task<List<LlmStreamEvent>> RunAsync(
        DeepSeekLlmService service, ConversationSession session, IReadOnlyList<ToolDefinition> tools)
    {
        var events = new List<LlmStreamEvent>();
        var sw = Stopwatch.StartNew();
        TimeSpan? firstDelta = null;

        await foreach (var e in service.StreamAsync(session.Messages, tools, CancellationToken.None))
        {
            if (e is LlmTextDelta && firstDelta is null) firstDelta = sw.Elapsed;
            events.Add(e);
            _output.WriteLine($"{sw.ElapsedMilliseconds,6} ms  {e}");
        }

        _output.WriteLine($"Первая дельта: {firstDelta?.TotalMilliseconds:F0} ms, всего: {sw.ElapsedMilliseconds} ms");
        return events;
    }

    private DeepSeekLlmService? CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var options = config.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _output.WriteLine("Llm:ApiKey не задан — пропускаем.");
            return null;
        }

        var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", options.ApiKey);
        return new DeepSeekLlmService(http, Options.Create(options));
    }
}
