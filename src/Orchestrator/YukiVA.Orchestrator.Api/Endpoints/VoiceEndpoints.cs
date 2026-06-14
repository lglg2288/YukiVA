using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

namespace YukiVA.Orchestrator.Api.Endpoints;

public static class VoiceEndpoints
{
    /// <summary>
    /// Голосовой API: один POST для приёма аудио и ответа аудио, плюс GET для получения истории сообщений текстом.
    /// </summary>
    /// <param name="app"></param>
    public static void MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/voice", async (
            HttpRequest request,
            HttpResponse response,
            ProcessVoiceTurnHandler handler,
            Guid? sessionId,
            CancellationToken ct) =>
        {
            using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, ct);
            var audio = ms.ToArray();

            if (audio.Length == 0)
                return Results.BadRequest("Пустое аудио.");

            var result = await handler.HandleAsync(audio, sessionId, ct);

            response.Headers["X-Session-Id"] = result.SessionPublicId.ToString();
            response.Headers["X-User-Text"] = System.Net.WebUtility.UrlEncode(result.UserText);
            response.Headers["X-Assistant-Text"] = System.Net.WebUtility.UrlEncode(result.AssistantText);

            return Results.File(result.AudioReply, "audio/wav");
        });

        app.MapGet("/api/sessions/{publicId:guid}/messages", async (
            Guid publicId,
            ISessionRepository sessions,
            CancellationToken ct) =>
        {
            var session = await sessions.GetByPublicIdAsync(publicId, ct);
            if (session is null)
                return Results.NotFound();

            var messages = session.Messages
                .Select(m => new { role = m.Role.ToString(), text = m.Text, at = m.CreatedAt })
                .ToList();

            return Results.Ok(new { sessionId = session.PublicId, messages });
        });
    }
}
