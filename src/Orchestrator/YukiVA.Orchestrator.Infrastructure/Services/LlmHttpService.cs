using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Domain.ValueObjects;
using YukiVA.Orchestrator.Infrastructure.Options;

namespace YukiVA.Orchestrator.Infrastructure.Services;

internal sealed class LlmHttpService(
    HttpClient httpClient,
    IOptions<LlmOptions> options,
    ILogger<LlmHttpService> logger) : ILlmService
{
    public async Task<LlmResult> GenerateAsync(
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default)
    {
        var request = new LlmRequestDto(
            Model: options.Value.Model,
            Messages: [.. history.Select(m => new MessageDto(
                Role: m.Role.ToString().ToLowerInvariant(),
                Content: m.Content,
                ToolCallId: m.ToolCallId))],
            Tools: [.. tools.Select(t => new ToolDto(t.Name, t.Description, t.InputSchemaJson))]
        );

        logger.LogDebug("POST /generate ({MsgCount} messages, {ToolCount} tools)",
            request.Messages.Length, request.Tools.Length);

        var response = await httpClient.PostAsJsonAsync("/generate", request, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<LlmResponseDto>(ct)
            ?? throw new InvalidOperationException("LLM service returned an empty response.");

        var toolCalls = dto.ToolCalls?.Select(tc =>
            new ToolCallRequest(tc.Id, tc.Name, tc.Arguments)).ToList()
            ?? [];

        return new LlmResult(dto.Text ?? string.Empty, toolCalls);
    }

    private sealed record LlmRequestDto(string Model, MessageDto[] Messages, ToolDto[] Tools);
    private sealed record MessageDto(string Role, string Content, string? ToolCallId);
    private sealed record ToolDto(string Name, string Description, string Schema);
    private sealed record LlmResponseDto(string? Text, List<ToolCallDto>? ToolCalls);
    private sealed record ToolCallDto(string Id, string Name, string Arguments);
}
