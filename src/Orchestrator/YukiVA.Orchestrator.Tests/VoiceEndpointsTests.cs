using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Tests;

/// <summary>SSE-эндпоинты целиком (маршрутизация, multipart, сериализация) на фейковых STT/LLM/TTS/БД.</summary>
public class VoiceEndpointsTests : IDisposable
{
    private const string ToolsJson = """[{"name":"query","description":"SQL","parametersJsonSchema":"{\"type\":\"object\"}"}]""";

    private readonly FakeLlm _llm = new();
    private readonly FakeTts _tts = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public VoiceEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Warmup:Enabled", "false"); // никаких запросов в облако из тестов
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ISpeechToText>(_ => new FakeStt("сколько пользователей?"));
                services.AddScoped<ILlmService>(_ => _llm);
                services.AddScoped<ITextToSpeech>(_ => _tts);
                services.AddSingleton<ISessionRepository, InMemorySessions>();
            });
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Voice_TextAnswer_StreamsEventsInContractOrder()
    {
        _llm.Turns.Enqueue(Text("Пользователей сейчас ровно сорок два."));

        var events = await PostVoiceAsync();

        Assert.Equal(
            new[] { "session", "audioFormat", "transcript" },
            events.Take(3).Select(e => e.EventType));
        Assert.Equal("done", events[^1].EventType);
        Assert.Equal("{}", events[^1].Data);

        Assert.Equal(24000, Json(events[1]).GetProperty("sampleRate").GetInt32());
        Assert.Equal("сколько пользователей?", Json(events[2]).GetProperty("text").GetString());
        Assert.Equal("Пользователей сейчас ровно сорок два.",
            string.Concat(events.Where(e => e.EventType == "text").Select(e => Json(e).GetProperty("delta").GetString())));

        var audio = events.Where(e => e.EventType == "audio").Select(Json).ToList();
        Assert.Equal(new[] { 0, 1 }, audio.Select(a => a.GetProperty("seq").GetInt32()));
        Assert.Equal("Пользователей сейчас ровно сорок два.#1",
            Encoding.UTF8.GetString(Convert.FromBase64String(audio[1].GetProperty("pcm").GetString()!)));

        // История сохранилась и видна через GET.
        var sessionId = Json(events[0]).GetProperty("sessionId").GetGuid();
        var history = await _client.GetFromJsonAsync<JsonElement>($"/api/sessions/{sessionId}/messages");
        Assert.Equal(2, history.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Voice_TwoToolCalls_ThenToolResults_ContinuesTurn()
    {
        _llm.Turns.Enqueue(Text("Сейчас посмотрю в базе данных.")
            .Append(new LlmToolCalls(new[]
            {
                new ToolCall("call_a", "query", """{"sql":"select 1"}"""),
                new ToolCall("call_b", "query", """{"sql":"select 2"}""")
            })));

        var first = await PostVoiceAsync();

        Assert.Equal(new[] { "toolCalls", "done" }, first.TakeLast(2).Select(e => e.EventType));
        var calls = Json(first[^2]).GetProperty("calls");
        Assert.Equal(2, calls.GetArrayLength());
        Assert.Equal("call_a", calls[0].GetProperty("id").GetString());
        Assert.Equal("query", calls[0].GetProperty("name").GetString());
        Assert.Equal("""{"sql":"select 1"}""", calls[0].GetProperty("arguments").GetString());

        var sessionId = Json(first[0]).GetProperty("sessionId").GetGuid();
        _llm.Turns.Enqueue(Text("Первый запрос вернул один, второй два."));

        using var response = await _client.PostAsJsonAsync("/api/voice/tool-results", new
        {
            sessionId,
            results = new[] { new { toolCallId = "call_a", content = "1" }, new { toolCallId = "call_b", content = "2" } },
            toolsJson = ToolsJson
        });
        var second = await ReadSseAsync(response);

        Assert.Equal(new[] { "session", "audioFormat" }, second.Take(2).Select(e => e.EventType));
        Assert.DoesNotContain(second, e => e.EventType == "toolCalls");
        Assert.Equal("done", second[^1].EventType);
    }

    [Fact]
    public async Task ToolResults_NotForAllCalls_Returns400BeforeStreaming()
    {
        _llm.Turns.Enqueue(new LlmStreamEvent[]
        {
            new LlmToolCalls(new[] { new ToolCall("call_a", "query", "{}"), new ToolCall("call_b", "query", "{}") }),
            new LlmCompleted("tool_calls")
        });
        var first = await PostVoiceAsync();
        var sessionId = Json(first[0]).GetProperty("sessionId").GetGuid();

        using var response = await _client.PostAsJsonAsync("/api/voice/tool-results", new
        {
            sessionId,
            results = new[] { new { toolCallId = "call_a", content = "1" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("call_b", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Voice_UnknownSession_Returns404()
    {
        using var response = await _client.PostAsync($"/api/voice?sessionId={Guid.NewGuid()}", VoiceForm());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Voice_BadToolsJson_Returns400()
    {
        using var response = await _client.PostAsync("/api/voice", VoiceForm(tools: "not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Voice_ErrorMidStream_SendsErrorThenDone()
    {
        _tts.FailWith = new InvalidOperationException("TTS недоступен");
        _llm.Turns.Enqueue(Text("Этот ответ не получится озвучить."));

        using var response = await _client.PostAsync("/api/voice", VoiceForm());
        var events = await ReadSseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // статус уже ушёл до ошибки
        Assert.Equal(new[] { "error", "done" }, events.TakeLast(2).Select(e => e.EventType));
        Assert.Equal("TTS недоступен", Json(events[^2]).GetProperty("message").GetString());

        // Сессия из первого события существует — клиент может продолжить в ней.
        var sessionId = Json(events[0]).GetProperty("sessionId").GetGuid();
        using var next = await _client.PostAsync($"/api/voice?sessionId={sessionId}", VoiceForm());
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Voice_UnexpectedError_HidesDetails()
    {
        _tts.FailWith = new HttpRequestException("ElevenLabs вернул 401: секретные подробности");
        _llm.Turns.Enqueue(Text("Этот ответ не получится озвучить."));

        var events = await PostVoiceAsync();

        var message = Json(events[^2]).GetProperty("message").GetString();
        Assert.DoesNotContain("секретные", message);
    }

    [Fact]
    public async Task Voice_EventsArriveBeforeLlmFinishes()
    {
        var gate = new TaskCompletionSource();
        _llm.Turns.Enqueue(Gated(gate.Task));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/voice") { Content = VoiceForm() };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var seen = new List<string>();
        await foreach (var e in SseParser.Create(await response.Content.ReadAsStreamAsync()).EnumerateAsync())
        {
            seen.Add(e.EventType);
            // Звук первого предложения дошёл до клиента, пока LLM ещё «думает» над вторым.
            if (e.EventType == "audio") gate.TrySetResult();
        }

        Assert.True(seen.IndexOf("audio") < seen.LastIndexOf("text"));
        Assert.Equal("done", seen[^1]);
    }

    private async Task<List<SseItem<string>>> PostVoiceAsync()
    {
        using var response = await _client.PostAsync("/api/voice", VoiceForm());
        return await ReadSseAsync(response);
    }

    private static async Task<List<SseItem<string>>> ReadSseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = new List<SseItem<string>>();
        await foreach (var e in SseParser.Create(await response.Content.ReadAsStreamAsync()).EnumerateAsync())
            events.Add(e);
        return events;
    }

    private static MultipartFormDataContent VoiceForm(string tools = ToolsJson) => new()
    {
        { new ByteArrayContent(new byte[] { 1, 2, 3 }), "audio", "speech.wav" },
        { new StringContent(tools), "tools" }
    };

    private static JsonElement Json(SseItem<string> e) => JsonDocument.Parse(e.Data).RootElement;

    private static List<LlmStreamEvent> Text(string text) =>
        new() { new LlmTextDelta(text), new LlmCompleted("stop") };

    private static async IAsyncEnumerable<LlmStreamEvent> Gated(
        Task gate, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new LlmTextDelta("Первое предложение уже готово. ");
        await gate.WaitAsync(TimeSpan.FromSeconds(5), ct);
        yield return new LlmTextDelta("Второе пришло позже.");
        yield return new LlmCompleted("stop");
    }

    private sealed class FakeStt(string text) : ISpeechToText
    {
        public Task<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default) =>
            Task.FromResult(text);
    }

    private sealed class FakeLlm : ILlmService
    {
        public Queue<object> Turns { get; } = new();

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            IReadOnlyList<Message> history, IReadOnlyList<ToolDefinition> tools,
            [EnumeratorCancellation] CancellationToken ct)
        {
            Assert.Equal("query", Assert.Single(tools).Name);

            var turn = Turns.Dequeue();
            if (turn is IAsyncEnumerable<LlmStreamEvent> stream)
            {
                await foreach (var e in stream.WithCancellation(ct))
                    yield return e;
                yield break;
            }

            foreach (var e in (IEnumerable<LlmStreamEvent>)turn)
            {
                await Task.Yield();
                yield return e;
            }
        }
    }

    private sealed class FakeTts : ITextToSpeech
    {
        public Exception? FailWith { get; set; }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
            string text, [EnumeratorCancellation] CancellationToken ct)
        {
            if (FailWith is not null)
                throw FailWith;

            for (var i = 0; i < 2; i++)
            {
                await Task.Yield();
                yield return Encoding.UTF8.GetBytes($"{text}#{i}");
            }
        }
    }

    private sealed class InMemorySessions : ISessionRepository
    {
        private readonly List<ConversationSession> _sessions = new();

        public Task AddAsync(ConversationSession session, CancellationToken cancellationToken = default)
        {
            lock (_sessions) _sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<ConversationSession?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default)
        {
            lock (_sessions) return Task.FromResult(_sessions.FirstOrDefault(s => s.PublicId == publicId));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
