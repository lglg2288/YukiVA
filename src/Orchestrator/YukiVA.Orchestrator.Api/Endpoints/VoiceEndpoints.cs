using System.Text;
using System.Text.Json;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Api.Endpoints;


public static class VoiceEndpoints
{
    public static void MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/voice", async (
            HttpRequest request,
            HttpResponse response,
            ProcessVoiceTurnHandler handler,
            Guid? sessionId,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Ожидается multipart/form-data.");

            var form = await request.ReadFormAsync(ct);

            var audioFile = form.Files.GetFile("audio");
            if (audioFile is null || audioFile.Length == 0)
                return Results.BadRequest("Отсутствует аудио (часть 'audio').");

            byte[] audio;
            using (var ms = new MemoryStream())
            {
                await audioFile.CopyToAsync(ms, ct);
                audio = ms.ToArray();
            }

            var tools = ParseTools(form["tools"]);

            return await WriteLegacyResultAsync(response, handler.HandleVoiceAsync(audio, sessionId, tools, ct));
        });

        app.MapPost("/api/voice/tool-result", async (
            HttpResponse response,
            ProcessVoiceTurnHandler handler,
            ISessionRepository sessions,
            ToolResultRequest body,
            CancellationToken ct) =>
        {
            var tools = ParseTools(body.ToolsJson);

            // Старый клиент шлёт один результат без id — отвечаем на единственный висящий вызов.
            var toolCallId = body.ToolCallId;
            if (toolCallId is null)
            {
                var session = await sessions.GetByPublicIdAsync(body.SessionId, ct);
                if (session?.PendingToolCalls is not { Count: 1 } pending)
                    return Results.BadRequest("Не указан toolCallId, а висящих вызовов не один.");
                toolCallId = pending[0].CallId;
            }

            var results = new[] { new ToolResult(toolCallId, body.Result) };
            return await WriteLegacyResultAsync(response, handler.HandleToolResultsAsync(body.SessionId, results, tools, ct));
        });

        app.MapGet("/api/sessions/{publicId:guid}/messages", async (
            Guid publicId,
            ISessionRepository sessions,
            CancellationToken ct) =>
        {
            var session = await sessions.GetByPublicIdAsync(publicId, ct);
            if (session is null) return Results.NotFound();

            var messages = session.Messages
                .Select(m => new
                {
                    role = m.Role.ToString(),
                    text = m.Text,
                    toolCallId = m.ToolCallId,
                    toolCalls = m.ToolCalls.Select(c => new { id = c.CallId, name = c.Name, arguments = c.ArgumentsJson }),
                    at = m.CreatedAt
                })
                .ToList();

            return Results.Ok(new { sessionId = session.PublicId, messages });
        });
    }

    // Временный мост до SSE (шаг 5): собираем поток событий в прежний ответ —
    // JSON с первым вызовом инструмента либо WAV с заголовком X-Assistant-Text.
    private static async Task<IResult> WriteLegacyResultAsync(
        HttpResponse response, IAsyncEnumerable<TurnEvent> events)
    {
        var text = new StringBuilder();
        using var pcm = new MemoryStream();
        IReadOnlyList<ToolCall> toolCalls = Array.Empty<ToolCall>();

        await foreach (var e in events)
        {
            switch (e)
            {
                case SessionStarted started: response.Headers["X-Session-Id"] = started.SessionId.ToString(); break;
                case AssistantTextDelta delta: text.Append(delta.Text); break;
                case AudioChunk chunk: pcm.Write(chunk.Pcm.Span); break;
                case ToolCallsRequested requested: toolCalls = requested.Calls; break;
            }
        }

        if (toolCalls.Count > 0)
        {
            return Results.Json(new
            {
                type = "tool_call",
                sessionId = response.Headers["X-Session-Id"].ToString(),
                toolCallId = toolCalls[0].CallId,
                name = toolCalls[0].Name,
                arguments = toolCalls[0].ArgumentsJson
            });
        }

        response.Headers["X-Assistant-Text"] = System.Net.WebUtility.UrlEncode(text.ToString());
        return Results.File(Wav.Write(pcm.ToArray(), AudioFormat.Pcm16Mono24k), "audio/wav");
    }

    private static IReadOnlyList<ToolDefinition> ParseTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ToolDefinition>();

        return JsonSerializer.Deserialize<List<ToolDefinition>>(json)
            ?? new List<ToolDefinition>();
    }

    private record ToolResultRequest(
        Guid SessionId,
        string? ToolCallId,
        string Result,
        string? ToolsJson);
}
