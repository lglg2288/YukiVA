using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;
using YukiVA.Orchestrator.Infrastructure.Options;

namespace YukiVA.Orchestrator.Infrastructure.Services;

public class DeepSeekLlmService : ILlmService
{
    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public DeepSeekLlmService(HttpClient http, IOptions<LlmOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<Message> history,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", _options.SystemPrompt)
        };

        foreach (var m in history)
        {
            messages.Add(new ChatMessage(MapRole(m.Role), m.Text));
        }

        var request = new ChatRequest(_options.Model, messages);

        var response = await _http.PostAsJsonAsync("/chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);

        return result?.Choices.FirstOrDefault()?.Message.Content
            ?? throw new InvalidOperationException("Пустой ответ от LLM.");
    }

    private static string MapRole(MessageRole role) => role switch
    {
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.System => "system",
        MessageRole.Tool => "tool",
        _ => "user"
    };


    private record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages);

    private record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private record ChatResponse(
        [property: JsonPropertyName("choices")] List<Choice> Choices);

    private record Choice(
        [property: JsonPropertyName("message")] ChatMessage Message);
}
