using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Models;
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

    public async Task<LlmResult> CompleteAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = _options.SystemPrompt }
        };

        foreach (var m in history)
        {
            messages.Add(new ChatMessage()
                        { Role = MapRole(m.Role), Content = m.Text });
        }

        List<Tool>? tools = null;
        if (availableTools.Count > 0)
        {
            tools = availableTools.Select(t => new Tool
            {
                Function = new FunctionDef
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
                }
            }).ToList();
        }

        var request = new ChatRequest()
        {
            Model = _options.Model,
            Messages = messages,
            Tools = tools
        };

        var response = await _http.PostAsJsonAsync("/chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Пустой ответ от LLM.");

        var choice = result.Choices.FirstOrDefault()
            ?? throw new InvalidOperationException("LLM не вернула ни одного варианта ответа.");

        var toolCall = choice.Message.ToolCalls?.FirstOrDefault();
        if (toolCall is not null)
        {
            return LlmResult.FromToolCall(
                toolCall.Id,
                toolCall.Function.Name,
                toolCall.Function.Arguments);
        }

        return LlmResult.FromText(choice.Message.Content ?? "");
    }

    private static string MapRole(MessageRole role) => role switch
    {
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.System => "system",
        MessageRole.Tool => "tool",
        _ => "user"
    };

    
    // DTOs

    private class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Tool>? Tools { get; set; }
    }

    private class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    private class Tool
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public FunctionDef Function { get; set; } = new();
    }

    private class FunctionDef
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("parameters")] public JsonElement Parameters { get; set; }
    }

    private class ToolCall
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("function")] public ToolCallFunction Function { get; set; } = new();
    }

    private class ToolCallFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "";
    }

    private class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice> Choices { get; set; } = new();
    }

    private class Choice
    {
        [JsonPropertyName("message")] public ChatMessage Message { get; set; } = new();
    }
}
