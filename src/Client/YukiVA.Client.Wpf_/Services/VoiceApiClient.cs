using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using YukiVA.Client.Wpf.Models;

namespace YukiVA.Client.Wpf.Services;

/// <summary>
/// Единственное место, где приложение общается с HTTP-API оркестратора.
/// ViewModel не знает про HttpClient — он просто вызывает методы этого класса.
/// </summary>
public sealed class VoiceApiClient
{
    private readonly HttpClient _http;

    public VoiceApiClient(HttpClient http) => _http = http;

    /// <summary>
    /// POST /api/voice — отправляем записанный звук (multipart/form-data) и список инструментов.
    /// </summary>
    public async Task<VoiceTurnResult> SendVoiceAsync(
        byte[] wav, Guid? sessionId, string toolsJson, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();

        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "audio", "speech.wav");   // часть формы "audio" (её ждёт сервер)

        content.Add(new StringContent(toolsJson), "tools"); // часть формы "tools"

        var url = "/api/voice";
        if (sessionId is Guid sid) url += "?sessionId=" + sid;

        using var resp = await _http.PostAsync(url, content, ct);
        await EnsureOkAsync(resp, ct);
        return await ParseAsync(resp, ct);
    }

    /// <summary>
    /// POST /api/voice/tool-result — возвращаем серверу результат выполнения инструмента.
    /// Имена полей в нижнем регистре (sessionId/result/toolsJson) — так их ждёт минимальный API.
    /// </summary>
    public async Task<VoiceTurnResult> SendToolResultAsync(
        Guid? sessionId, string result, string toolsJson, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("/api/voice/tool-result", new
        {
            sessionId,
            result,
            toolsJson
        }, ct);
        await EnsureOkAsync(resp, ct);
        return await ParseAsync(resp, ct);
    }

    /// <summary>
    /// GET /api/sessions/{id}/messages — вся история переписки сессии.
    /// </summary>
    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid sessionId, CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync($"/api/sessions/{sessionId}/messages", ct);

        using var doc = JsonDocument.Parse(json);
        var result = new List<ChatMessage>();
        foreach (var m in doc.RootElement.GetProperty("messages").EnumerateArray())
        {
            result.Add(new ChatMessage
            {
                Role = m.GetProperty("role").GetString() ?? "",
                Text = m.GetProperty("text").GetString() ?? "",
                At = m.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var dt)
                    ? dt
                    : default
            });
        }
        return result;
    }

    /// <summary>
    /// Проверяем код ответа. Если это НЕ 2xx — читаем тело (там сервер пишет детали ошибки)
    /// и кидаем понятное исключение с этим текстом, а не голое "500".
    /// </summary>
    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        string body = "";
        try { body = await resp.Content.ReadAsStringAsync(ct); } catch { /* тела может не быть */ }

        var code = (int)resp.StatusCode;
        var shortBody = body.Length > 800 ? body[..800] + "…" : body;
        throw new HttpRequestException(
            $"Сервер вернул {code} ({resp.ReasonPhrase}). Ответ: {shortBody}");
    }

    /// <summary>
    /// Разбираем ответ сервера. Если Content-Type = application/json — это вызов инструмента,
    /// иначе это аудио-ответ (audio/wav) + текст в заголовке X-Assistant-Text.
    /// </summary>
    private static async Task<VoiceTurnResult> ParseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        // Id сессии сервер всегда кладёт в заголовок.
        Guid? sessionId = null;
        if (resp.Headers.TryGetValues("X-Session-Id", out var ids)
            && Guid.TryParse(ids.FirstOrDefault(), out var sid))
            sessionId = sid;

        bool isJson = resp.Content.Headers.ContentType?.MediaType == "application/json";

        if (isJson)
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new VoiceTurnResult
            {
                SessionId = sessionId,
                IsToolCall = true,
                ToolName = root.GetProperty("name").GetString(),
                ToolCallId = root.TryGetProperty("toolCallId", out var tc) ? tc.GetString() : null,
                // "arguments" может быть строкой с JSON внутри — сохраняем как есть.
                ToolArgumentsJson = root.TryGetProperty("arguments", out var a) ? a.GetRawText() : "{}"
            };
        }

        var audio = await resp.Content.ReadAsByteArrayAsync(ct);
        string? text = null;
        if (resp.Headers.TryGetValues("X-Assistant-Text", out var texts))
            text = WebUtility.UrlDecode(texts.FirstOrDefault() ?? "");

        return new VoiceTurnResult
        {
            SessionId = sessionId,
            IsToolCall = false,
            AssistantText = text,
            AudioReply = audio
        };
    }
}
