using System.Text.Json;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

namespace YukiVA.Orchestrator.Api.Endpoints;

public static class VoiceEndpoints
{
    public static void MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        // Голосовой ход: multipart (audio + tools) → поток SSE-событий.
        app.MapPost("/api/voice", async (
            HttpRequest request,
            ProcessVoiceTurnHandler handler,
            ISessionRepository sessions,
            ILoggerFactory loggers,
            Guid? sessionId,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Ожидается multipart/form-data.");

            var form = await request.ReadFormAsync(ct);

            var audioFile = form.Files.GetFile("audio");
            if (audioFile is null || audioFile.Length == 0)
                return Results.BadRequest("Отсутствует аудио (часть 'audio').");

            if (!TryParseTools(form["tools"], out var tools))
                return Results.BadRequest("Часть 'tools' — не JSON-массив ToolDefinition.");

            // Пока поток не начат, неизвестную сессию ещё можно вернуть честным 404.
            if (sessionId is Guid id && await sessions.GetByPublicIdAsync(id, ct) is null)
                return Results.NotFound($"Сессия {id} не найдена.");

            byte[] audio;
            using (var ms = new MemoryStream())
            {
                await audioFile.CopyToAsync(ms, ct);
                audio = ms.ToArray();
            }

            return TypedResults.ServerSentEvents(TurnEventStream.ToSseAsync(
                handler.HandleVoiceAsync(audio, sessionId, tools, ct),
                loggers.CreateLogger("YukiVA.Orchestrator.VoiceTurn"),
                ct));
        });

        // Продолжение хода: результаты ВСЕХ запрошенных вызовов → поток SSE-событий.
        app.MapPost("/api/voice/tool-results", async (
            ToolResultsRequest body,
            ProcessVoiceTurnHandler handler,
            ISessionRepository sessions,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            if (!TryParseTools(body.ToolsJson, out var tools))
                return Results.BadRequest("Поле 'toolsJson' — не JSON-массив ToolDefinition.");

            var session = await sessions.GetByPublicIdAsync(body.SessionId, ct);
            if (session is null)
                return Results.NotFound($"Сессия {body.SessionId} не найдена.");

            var results = body.Results.Select(r => new ToolResult(r.ToolCallId, r.Content)).ToList();
            var pending = session.PendingToolCalls.Select(c => c.CallId).ToHashSet();
            if (!pending.SetEquals(results.Select(r => r.ToolCallId)) || results.Count != pending.Count)
                return Results.BadRequest(
                    $"Нужны результаты ровно для вызовов: {string.Join(", ", pending)}.");

            return TypedResults.ServerSentEvents(TurnEventStream.ToSseAsync(
                handler.HandleToolResultsAsync(body.SessionId, results, tools, ct),
                loggers.CreateLogger("YukiVA.Orchestrator.VoiceTurn"),
                ct));
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

    private static bool TryParseTools(string? json, out IReadOnlyList<ToolDefinition> tools)
    {
        tools = Array.Empty<ToolDefinition>();
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            // Web-настройки: camelCase и PascalCase одинаково.
            tools = JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonSerializerOptions.Web)
                ?? new List<ToolDefinition>();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private record ToolResultsRequest(
        Guid SessionId,
        IReadOnlyList<ToolResultDto> Results,
        string? ToolsJson);

    private record ToolResultDto(string ToolCallId, string Content);
}
