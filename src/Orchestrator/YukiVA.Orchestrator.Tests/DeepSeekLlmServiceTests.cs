using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Tests;

public class DeepSeekLlmServiceTests
{
    [Fact]
    public async Task StreamAsync_TextOnly_YieldsDeltasThenCompleted()
    {
        var handler = new FakeSseHandler(
            """{"choices":[{"delta":{"role":"assistant","content":""},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"content":"При"},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"content":"вет!"},"finish_reason":null}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"total_tokens":10}}""");

        var events = await CollectAsync(CreateService(handler), UserHistory("привет"));

        Assert.Equal(new LlmStreamEvent[]
        {
            new LlmTextDelta("При"),
            new LlmTextDelta("вет!"),
            new LlmCompleted("stop")
        }, events);
    }

    [Fact]
    public async Task StreamAsync_TextAndTwoChunkedToolCalls_GluesCallsByIndex()
    {
        var handler = new FakeSseHandler(
            """{"choices":[{"delta":{"content":"Сейчас посмотрю."}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","type":"function","function":{"name":"query","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"sql\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":1,"id":"call_b","type":"function","function":{"name":"list_tables","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"select 1\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");

        var events = await CollectAsync(CreateService(handler), UserHistory("что в базе?"));

        Assert.Equal(3, events.Count);
        Assert.Equal(new LlmTextDelta("Сейчас посмотрю."), events[0]);
        var calls = Assert.IsType<LlmToolCalls>(events[1]).Calls;
        Assert.Equal(new[]
        {
            new ToolCall("call_a", "query", """{"sql":"select 1"}"""),
            new ToolCall("call_b", "list_tables", "{}")
        }, calls);
        Assert.Equal(new LlmCompleted("tool_calls"), events[2]);
    }

    [Fact]
    public async Task StreamAsync_ReasoningContent_IsNotYieldedAsText()
    {
        var handler = new FakeSseHandler(
            """{"choices":[{"delta":{"reasoning_content":"Пользователь здоровается…"}}]}""",
            """{"choices":[{"delta":{"content":"Привет!"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""");

        var events = await CollectAsync(CreateService(handler), UserHistory("привет"));

        Assert.Equal(new LlmTextDelta("Привет!"), Assert.Single(events.OfType<LlmTextDelta>()));
    }

    [Fact]
    public async Task StreamAsync_SerializesHistoryWithToolCallsAndResults()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage(null, new[]
        {
            new ToolCall("call_a", "query", """{"sql":"select 1"}"""),
            new ToolCall("call_b", "list_tables", "{}")
        });
        session.AddToolResult("call_a", "1");
        session.AddToolResult("call_b", "users");

        var handler = new FakeSseHandler("""{"choices":[{"delta":{},"finish_reason":"stop"}]}""");
        var tools = new[] { new ToolDefinition("query", "SQL", """{"type":"object"}""") };

        await CollectAsync(CreateService(handler), session.Messages, tools);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var root = body.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("query", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(5, messages.GetArrayLength()); // system + 4

        var assistant = messages[2];
        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, assistant.GetProperty("content").ValueKind);
        var calls = assistant.GetProperty("tool_calls");
        Assert.Equal(2, calls.GetArrayLength());
        Assert.Equal("call_a", calls[0].GetProperty("id").GetString());
        Assert.Equal("function", calls[0].GetProperty("type").GetString());
        Assert.Equal("query", calls[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("""{"sql":"select 1"}""", calls[0].GetProperty("function").GetProperty("arguments").GetString());

        var toolB = messages[4];
        Assert.Equal("tool", toolB.GetProperty("role").GetString());
        Assert.Equal("call_b", toolB.GetProperty("tool_call_id").GetString());
        Assert.Equal("users", toolB.GetProperty("content").GetString());
        Assert.False(toolB.TryGetProperty("tool_calls", out _));

        Assert.DoesNotContain("[tool_call:", handler.RequestBody);
    }

    [Fact]
    public async Task StreamAsync_ErrorStatus_ThrowsWithBody()
    {
        var handler = new FakeSseHandler(HttpStatusCode.BadRequest, """{"error":{"message":"bad tool_call_id"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CollectAsync(CreateService(handler), UserHistory("привет")));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("bad tool_call_id", ex.Message);
    }

    private static DeepSeekLlmService CreateService(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.deepseek.test") },
            Options.Create(new LlmOptions { Model = "deepseek-flash", SystemPrompt = "system" }));

    private static IReadOnlyList<Message> UserHistory(string text)
    {
        var session = new ConversationSession();
        session.AddUserMessage(text);
        return session.Messages;
    }

    private static async Task<List<LlmStreamEvent>> CollectAsync(
        DeepSeekLlmService service,
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition>? tools = null)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var e in service.StreamAsync(history, tools ?? Array.Empty<ToolDefinition>(), CancellationToken.None))
            events.Add(e);
        return events;
    }

    /// <summary>Отдаёт заготовленный SSE-поток DeepSeek и запоминает тело запроса.</summary>
    private sealed class FakeSseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public string? RequestBody { get; private set; }

        public FakeSseHandler(params string[] chunks)
        {
            _status = HttpStatusCode.OK;
            // Как у DeepSeek: keep-alive комментарий, события через пустую строку, в конце [DONE].
            _body = ": keep-alive\n\n"
                + string.Concat(chunks.Select(c => $"data: {c}\n\n"))
                + "data: [DONE]\n\n";
        }

        public FakeSseHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/chat/completions", request.RequestUri!.AbsolutePath);
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
