using Microsoft.Extensions.Options;

namespace YukiVA.Orchestrator.Api.Security;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiKeyOptions _options;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_options.Key))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/health") || path.StartsWith("/swagger") || path.StartsWith("/openapi"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(_options.HeaderName, out var provided) || provided != _options.Key )
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Неверный или отсутствующий API-ключ." });
            return;
        }

        await _next(context);
    }
}
