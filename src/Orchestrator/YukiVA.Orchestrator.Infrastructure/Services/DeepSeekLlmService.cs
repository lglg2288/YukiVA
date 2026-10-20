using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
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

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(BuildRequest(history, tools))
        };

        // ResponseHeadersRead — не ждать всё тело, читать чанки по мере прихода.
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"DeepSeek вернул {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(ct);

        // Вызовы приходят кусками: id и name в первом, arguments дописываются. Ключ — index.
        var toolCalls = new SortedDictionary<int, ToolCallBuilder>();
        string? finishReason = null;

        await foreach (var item in SseParser.Create(body).EnumerateAsync(ct))
        {
            if (item.Data == "[DONE]")
                break;

            var choice = JsonSerializer.Deserialize<ChatChunk>(item.Data)?.Choices.FirstOrDefault();
            if (choice is null)
                continue;

            // delta.reasoning_content (режим размышлений) не читаем — его не озвучиваем.
            if (!string.IsNullOrEmpty(choice.Delta?.Content))
                yield return new LlmTextDelta(choice.Delta.Content);

            foreach (var part in choice.Delta?.ToolCalls ?? [])
            {
                if (!toolCalls.TryGetValue(part.Index, out var call))
                    toolCalls[part.Index] = call = new ToolCallBuilder();

                call.Id ??= part.Id;
                call.Name ??= part.Function?.Name;
                call.Arguments.Append(part.Function?.Arguments);
            }

            finishReason = choice.FinishReason ?? finishReason;
        }

        if (toolCalls.Count > 0)
            yield return new LlmToolCalls(toolCalls.Values.Select(c => c.Build()).ToList());

        yield return new LlmCompleted(finishReason);
    }

    private ChatRequest BuildRequest(IReadOnlyList<Message> history, IReadOnlyList<ToolDefinition> tools)
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = _options.SystemPrompt }
        };

        foreach (var m in history)
        {
            messages.Add(new ChatMessage()
            {
                Role = MapRole(m.Role),
                Content = m.Text,
                ToolCallId = m.ToolCallId,
                ToolCalls = m.ToolCalls.Count == 0 ? null : m.ToolCalls.Select(c => new ChatToolCall
                {
                    Id = c.CallId,
                    Function = new ChatToolCallFunction { Name = c.Name, Arguments = c.ArgumentsJson }
                }).ToList()
            });
        }

        return new ChatRequest()
        {
            Model = _options.Model,
            Messages = messages,
            Tools = tools.Count == 0 ? null : tools.Select(t => new Tool
            {
                Function = new FunctionDef
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
                }
            }).ToList()
        };
    }

    private static string MapRole(MessageRole role) => role switch
    {
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.System => "system",
        MessageRole.Tool => "tool",
        _ => "user"
    };

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();

        public ToolCall Build() => new(
            Id ?? throw new InvalidOperationException("DeepSeek вернул вызов инструмента без id."),
            Name ?? throw new InvalidOperationException($"DeepSeek вернул вызов '{Id}' без имени."),
            Arguments.Length == 0 ? "{}" : Arguments.ToString());
    }

    // DTOs

    private class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Tool>? Tools { get; set; }
        [JsonPropertyName("stream")] public bool Stream { get; set; } = true;

        // Режим размышлений выключен: он задерживает первый токен, а с tools требует
        // возвращать reasoning_content во всех следующих запросах (иначе 400).
        [JsonPropertyName("thinking")] public Thinking Thinking { get; set; } = new();
    }

    private class Thinking
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "disabled";
    }

    private class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ChatToolCall>? ToolCalls { get; set; }
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; set; }
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

    private class ChatToolCall
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public ChatToolCallFunction Function { get; set; } = new();
    }

    private class ChatToolCallFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "";
    }

    // Чанки потока: choices[0].delta.{content, tool_calls[]}, finish_reason.

    private class ChatChunk
    {
        [JsonPropertyName("choices")] public List<ChunkChoice> Choices { get; set; } = new();
    }

    private class ChunkChoice
    {
        [JsonPropertyName("delta")] public ChunkDelta? Delta { get; set; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
    }

    private class ChunkDelta
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("tool_calls")] public List<ChunkToolCall>? ToolCalls { get; set; }
    }

    private class ChunkToolCall
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("function")] public ChunkToolCallFunction? Function { get; set; }
    }

    private class ChunkToolCallFunction
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    }
}
