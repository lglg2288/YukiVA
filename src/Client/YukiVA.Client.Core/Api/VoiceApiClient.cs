using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Core.Api;

/// <summary>HTTP-клиент оркестратора: запрос уходит целиком, ответ читается как поток SSE-событий.</summary>
public sealed class VoiceApiClient
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private readonly HttpClient _http;

    public VoiceApiClient(HttpClient http) => _http = http;

    /// <summary>Голосовой ход: WAV и список инструментов.</summary>
    public IAsyncEnumerable<ServerEvent> SendVoiceAsync(
        byte[] wav, Guid? sessionId, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        var form = new MultipartFormDataContent
        {
            { audio, "audio", "speech.wav" },
            { new StringContent(JsonSerializer.Serialize(tools, Json)), "tools" }
        };

        var url = sessionId is Guid id ? $"/api/voice?sessionId={id}" : "/api/voice";
        return StreamAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = form }, ct);
    }

    /// <summary>Продолжение хода: результаты всех вызовов разом.</summary>
    public IAsyncEnumerable<ServerEvent> SendToolResultsAsync(
        Guid sessionId, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
    {
        var body = new
        {
            sessionId,
            results = results.Select(r => new { toolCallId = r.ToolCallId, content = r.Content }),
            toolsJson = JsonSerializer.Serialize(tools, Json)
        };

        return StreamAsync(
            new HttpRequestMessage(HttpMethod.Post, "/api/voice/tool-results") { Content = JsonContent.Create(body) },
            ct);
    }

    private async IAsyncEnumerable<ServerEvent> StreamAsync(
        HttpRequestMessage request, [EnumeratorCancellation] CancellationToken ct)
    {
        using (request)
        {
            // ResponseHeadersRead — читать события по мере прихода, а не после конца ответа.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new VoiceApiException(response.StatusCode, await response.Content.ReadAsStringAsync(ct));

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            await foreach (var item in SseParser.Create(body).EnumerateAsync(ct))
            {
                if (Parse(item.EventType, item.Data) is { } e)
                    yield return e;
            }
        }
    }

    /// <summary>Неизвестные события пропускаем — сервер может добавить новые, не ломая клиент.</summary>
    internal static ServerEvent? Parse(string type, string data)
    {
        using var doc = JsonDocument.Parse(data);
        var json = doc.RootElement;

        return type switch
        {
            "session" => new SessionEvent(json.GetProperty("sessionId").GetGuid()),
            "audioFormat" => new AudioFormatEvent(
                json.GetProperty("sampleRate").GetInt32(),
                json.GetProperty("channels").GetInt32(),
                json.GetProperty("bitsPerSample").GetInt32()),
            "transcript" => new TranscriptEvent(json.GetProperty("text").GetString() ?? ""),
            "text" => new TextEvent(json.GetProperty("delta").GetString() ?? ""),
            "sentence" => new SentenceEvent(json.GetProperty("index").GetInt32(), json.GetProperty("text").GetString() ?? ""),
            "audio" => new AudioEvent(json.GetProperty("seq").GetInt32(), json.GetProperty("pcm").GetBytesFromBase64()),
            "toolCalls" => new ToolCallsEvent(json.GetProperty("calls").EnumerateArray()
                .Select(c => new ToolCall(
                    c.GetProperty("id").GetString()!,
                    c.GetProperty("name").GetString()!,
                    c.GetProperty("arguments").GetString() ?? "{}"))
                .ToList()),
            "error" => new ErrorEvent(json.GetProperty("message").GetString() ?? ""),
            "done" => new DoneEvent(),
            _ => null
        };
    }
}

/// <summary>Сервер отказал до начала потока (400/401/404/500).</summary>
public sealed class VoiceApiException(HttpStatusCode status, string body)
    : Exception($"Оркестратор ответил {(int)status}: {body}")
{
    public HttpStatusCode StatusCode { get; } = status;
}
