using YukiVA.Orchestrator.Domain.Interfaces;

namespace YukiVA.Orchestrator.Api.Endpoints;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/sessions")
            .WithTags("Sessions");

        group.MapPost("/", async (
            CreateSessionRequest request,
            ISessionRepository repository,
            CancellationToken ct) =>
        {
            var session = await repository.CreateAsync(request.UserId, ct);
            var response = new SessionResponse(session.Id, session.UserId, session.CreatedAt, session.State.ToString());
            return TypedResults.Created($"/api/sessions/{session.Id}", response);
        })
        .WithSummary("Create a new speech session");

        group.MapGet("/{id:guid}", async (
            Guid id,
            ISessionRepository repository,
            CancellationToken ct) =>
        {
            var session = await repository.GetAsync(id, ct);
            return session is null
                ? Results.NotFound()
                : Results.Ok(new SessionResponse(session.Id, session.UserId, session.CreatedAt, session.State.ToString()));
        })
        .WithSummary("Get session details");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISessionRepository repository,
            CancellationToken ct) =>
        {
            await repository.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .WithSummary("Delete a session");

        return app;
    }

    private sealed record CreateSessionRequest(string UserId);

    private sealed record SessionResponse(
        Guid Id,
        string UserId,
        DateTimeOffset CreatedAt,
        string State);
}
