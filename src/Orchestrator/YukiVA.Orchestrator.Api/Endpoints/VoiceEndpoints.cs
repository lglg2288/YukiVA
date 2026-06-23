using System.Text.Json;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;
using YukiVA.Orchestrator.Application.Models;

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

            var result = await handler.HandleVoiceAsync(audio, sessionId, tools, ct);
            return await WriteResultAsync(response, result);
        });

        app.MapPost("/api/voice/tool-result", async (
            HttpResponse response,
            ProcessVoiceTurnHandler handler,
            ToolResultRequest body,
            CancellationToken ct) =>
        {
            var tools = ParseTools(body.ToolsJson);

            var result = await handler.HandleToolResultAsync(
                body.SessionId, body.Result, tools, ct);
            return await WriteResultAsync(response, result);
        });

        app.MapGet("/api/sessions/{publicId:guid}/messages", async (
            Guid publicId,
            ISessionRepository sessions,
            CancellationToken ct) =>
        {
            var session = await sessions.GetByPublicIdAsync(publicId, ct);
            if (session is null) return Results.NotFound();

            var messages = session.Messages
                .Select(m => new { role = m.Role.ToString(), text = m.Text, at = m.CreatedAt })
                .ToList();

            return Results.Ok(new { sessionId = session.PublicId, messages });
        });
    }

    private static async Task<IResult> WriteResultAsync(
        HttpResponse response, ProcessVoiceTurnResult result)
    {
        response.Headers["X-Session-Id"] = result.SessionPublicId.ToString();

        if (result.IsToolCall)
        {
            return Results.Json(new
            {
                type = "tool_call",
                sessionId = result.SessionPublicId,
                toolCallId = result.ToolCallId,
                name = result.ToolName,
                arguments = result.ToolArgumentsJson
            });
        }

        response.Headers["X-Assistant-Text"] =
            System.Net.WebUtility.UrlEncode(result.AssistantText ?? "");
        return Results.File(result.AudioReply!, "audio/wav");
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
        string Result,
        string? ToolsJson);
}
