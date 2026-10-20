using System.Net;
using System.Text;
using System.Text.Json;
using YukiVA.Client.Core.Audio;
using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Core.Tests;

/// <summary>Отвечает заготовленными SSE-потоками по очереди и запоминает запросы.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<(string Method, string Path, string Body, string? ContentType)> Requests { get; } = new();

    public FakeServer Sse(params (string Event, object Data)[] events)
    {
        var body = string.Concat(events.Select(e => $"event: {e.Event}\ndata: {JsonSerializer.Serialize(e.Data)}\n\n"));
        _responses.Enqueue((HttpStatusCode.OK, body));
        return this;
    }

    public FakeServer Fail(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery,
            await request.Content!.ReadAsStringAsync(ct), request.Content.Headers.ContentType?.MediaType));

        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, status == HttpStatusCode.OK ? "text/event-stream" : "text/plain")
        };
    }

    public static (string, object) Session(Guid id) => ("session", new { sessionId = id });
    public static (string, object) Format(int rate = 24000) => ("audioFormat", new { sampleRate = rate, channels = 1, bitsPerSample = 16 });
    public static (string, object) Transcript(string text) => ("transcript", new { text });
    public static (string, object) Text(string delta) => ("text", new { delta });
    public static (string, object) Sentence(int index, string text) => ("sentence", new { index, text });
    public static (string, object) Chunk(int seq, int bytes) => ("audio", new { seq, pcm = Convert.ToBase64String(new byte[bytes]) });
    public static (string, object) Error(string message) => ("error", new { message });
    public static (string, object) Done() => ("done", new { });

    public static (string, object) ToolCalls(params (string Id, string Name, string Args)[] calls) =>
        ("toolCalls", new { calls = calls.Select(c => new { id = c.Id, name = c.Name, arguments = c.Args }) });
}

internal sealed class FakeAudio : IAudioOutput
{
    public long WrittenBytes { get; private set; }
    public long PlayedBytes { get; set; }
    public int Resets { get; private set; }

    public void Write(ReadOnlySpan<byte> pcm) => WrittenBytes += pcm.Length;

    public void Reset()
    {
        Resets++;
        WrittenBytes = 0;
        PlayedBytes = 0;
    }
}

internal sealed class FakeTools : IToolExecutor
{
    public List<ToolDefinition> Definitions { get; } = new() { new("execute_sql", "SQL", """{"type":"object"}""") };
    public Dictionary<string, string> Results { get; } = new();
    public List<ToolCall> Executed { get; } = new();

    public Task<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>(Definitions);

    public Task<string> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        Executed.Add(call);
        return Results.TryGetValue(call.Id, out var result)
            ? Task.FromResult(result)
            : Task.FromException<string>(new InvalidOperationException("relation \"nope\" does not exist"));
    }
}
