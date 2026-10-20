using System.Net;
using System.Text.Json;
using YukiVA.Client.Core.Api;
using YukiVA.Client.Core.Conversation;
using static YukiVA.Client.Core.Tests.FakeServer;

namespace YukiVA.Client.Core.Tests;

public class ConversationControllerTests
{
    private static readonly Guid SessionId = Guid.Parse("3f2b8c1e-0000-4000-8000-000000000001");

    private readonly FakeServer _server = new();
    private readonly FakeAudio _audio = new();
    private readonly FakeTools _tools = new();

    private ConversationController CreateController() =>
        new(new VoiceApiClient(new HttpClient(_server) { BaseAddress = new Uri("http://yuki.test") }), _tools, _audio);

    [Fact]
    public async Task TextTurn_StagesTextSentencesAudioAndTimings()
    {
        _server.Sse(Session(SessionId), Format(), Transcript("привет"),
            Text("Привет! Я Юки, "), Text("рада помочь."),
            Sentence(0, "Привет! Я Юки, рада помочь."), Chunk(0, 100), Chunk(1, 50), Done());
        var controller = CreateController();

        var updates = await CollectAsync(controller.RunTurnAsync(new byte[] { 1 }, default));

        Assert.Equal(SessionId, controller.SessionId);
        Assert.Equal(1, _audio.Resets);
        Assert.Equal(150, _audio.WrittenBytes);

        Assert.Equal(
            new[] { TurnStage.Recognizing, TurnStage.Thinking, TurnStage.Speaking },
            updates.OfType<StageChanged>().Select(s => s.Stage));
        Assert.Equal("привет", Assert.Single(updates.OfType<TranscriptReceived>()).Text);
        Assert.Equal(1, Assert.Single(updates.OfType<AssistantMessageStarted>()).Response);
        Assert.Equal("Привет! Я Юки, рада помочь.",
            string.Concat(updates.OfType<AssistantTextAppended>().Select(t => t.Delta)));

        var sentence = Assert.Single(updates.OfType<SentenceQueued>());
        Assert.Equal((1, 0, 27, 0L), (sentence.Response, sentence.CharStart, sentence.CharLength, sentence.AudioStartByte));

        var timings = Assert.IsType<TurnFinished>(updates[^1]).Timings;
        Assert.NotNull(timings.Transcript);
        Assert.NotNull(timings.FirstText);
        Assert.NotNull(timings.FirstAudio);
        Assert.True(timings.Transcript <= timings.FirstText && timings.FirstText <= timings.FirstAudio);

        // В лог — по одному куску звука на предложение, а не каждый.
        Assert.Single(updates.OfType<ServerEventLogged>(), l => l.Name == "audio");
    }

    [Fact]
    public async Task SecondSentence_StartsAtBytesAlreadyWritten()
    {
        _server.Sse(Session(SessionId), Format(), Transcript("расскажи"),
            Text("Первое предложение тут. Второе предложение там."),
            Sentence(0, "Первое предложение тут."), Chunk(0, 480), Chunk(1, 480),
            Sentence(1, "Второе предложение там."), Chunk(2, 480), Done());

        var updates = await CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default));

        var sentences = updates.OfType<SentenceQueued>().ToList();
        Assert.Equal(new[] { 0L, 960L }, sentences.Select(s => s.AudioStartByte));
        Assert.Equal(new[] { 0, 24 }, sentences.Select(s => s.CharStart));
    }

    [Fact]
    public async Task ToolCalls_ExecutesAll_SendsResults_ContinuesInNewBubble()
    {
        _tools.Results["call_a"] = "[{'count': 168}]";
        _tools.Results["call_b"] = "[{'name': 'Север'}]";
        _server
            .Sse(Session(SessionId), Format(), Transcript("заказы?"), Text("Сейчас посмотрю."),
                ToolCalls(("call_a", "execute_sql", """{"sql":"select 1"}"""), ("call_b", "execute_sql", """{"sql":"select 2"}""")),
                Done())
            .Sse(Session(SessionId), Format(), Text("За неделю сто шестьдесят восемь заказов."), Done());

        var updates = await CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default));

        Assert.Equal(new[] { "call_a", "call_b" }, _tools.Executed.Select(c => c.Id));
        Assert.Equal(new[] { 1, 2 }, updates.OfType<AssistantMessageStarted>().Select(m => m.Response));
        Assert.All(updates.OfType<ToolCallStarted>(), s => Assert.Equal(1, s.Response));
        Assert.All(updates.OfType<ToolCallFinished>(), f => Assert.False(f.Failed));

        var (method, path, body, contentType) = _server.Requests[1];
        Assert.Equal(("POST", "/api/voice/tool-results", "application/json"), (method, path, contentType));
        using var json = JsonDocument.Parse(body);
        Assert.Equal(SessionId, json.RootElement.GetProperty("sessionId").GetGuid());
        var results = json.RootElement.GetProperty("results");
        Assert.Equal("call_b", results[1].GetProperty("toolCallId").GetString());
        Assert.Equal("[{'name': 'Север'}]", results[1].GetProperty("content").GetString());
        Assert.Contains("execute_sql", json.RootElement.GetProperty("toolsJson").GetString());

        Assert.IsType<TurnFinished>(updates[^1]);
    }

    [Fact]
    public async Task ToolFailure_IsSentToModelAsText()
    {
        _server
            .Sse(Session(SessionId), ToolCalls(("call_x", "execute_sql", "{}")), Done())
            .Sse(Session(SessionId), Text("Не получилось выполнить запрос."), Done());

        var updates = await CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default));

        var finished = Assert.Single(updates.OfType<ToolCallFinished>());
        Assert.True(finished.Failed);
        Assert.Contains("does not exist", _server.Requests[1].Body);
        Assert.IsType<TurnFinished>(updates[^1]);
    }

    [Fact]
    public async Task EndlessToolCalls_StopsAfterMaxRounds()
    {
        _tools.Results["call"] = "ok";
        for (var i = 0; i < ConversationController.MaxToolRounds; i++)
            _server.Sse(Session(SessionId), ToolCalls(("call", "execute_sql", "{}")), Done());

        var updates = await CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default));

        Assert.Contains("ход остановлен", Assert.IsType<TurnFailed>(updates[^1]).Message);
        Assert.Equal(ConversationController.MaxToolRounds - 1, _tools.Executed.Count);
        Assert.Equal(ConversationController.MaxToolRounds, _server.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorEvent_FailsTurn()
    {
        _server.Sse(Session(SessionId), Format(), Error("Сервис распознавания недоступен"), Done());

        var updates = await CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default));

        Assert.Equal("Сервис распознавания недоступен", Assert.IsType<TurnFailed>(updates[^1]).Message);
    }

    [Fact]
    public async Task WrongAudioFormat_FailsTurn()
    {
        _server.Sse(Session(SessionId), Format(rate: 44100), Done());

        var updates = await CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default));

        Assert.Contains("44100", Assert.IsType<TurnFailed>(updates[^1]).Message);
    }

    [Fact]
    public async Task HttpError_Throws()
    {
        _server.Fail(HttpStatusCode.Unauthorized, "Неверный или отсутствующий API-ключ.");

        var ex = await Assert.ThrowsAsync<VoiceApiException>(
            () => CollectAsync(CreateController().RunTurnAsync(new byte[] { 1 }, default)));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task NextTurn_ReusesSession_NewConversationDropsIt()
    {
        _server.Sse(Session(SessionId), Done()).Sse(Session(SessionId), Done()).Sse(Session(Guid.NewGuid()), Done());
        var controller = CreateController();

        await CollectAsync(controller.RunTurnAsync(new byte[] { 1 }, default));
        await CollectAsync(controller.RunTurnAsync(new byte[] { 1 }, default));
        controller.StartNewConversation();
        await CollectAsync(controller.RunTurnAsync(new byte[] { 1 }, default));

        Assert.Equal("/api/voice", _server.Requests[0].Path);
        Assert.Equal($"/api/voice?sessionId={SessionId}", _server.Requests[1].Path);
        Assert.Equal("/api/voice", _server.Requests[2].Path);
        Assert.Equal("multipart/form-data", _server.Requests[0].ContentType);
        Assert.Contains("execute_sql", _server.Requests[0].Body);
    }

    private static async Task<List<TurnUpdate>> CollectAsync(IAsyncEnumerable<TurnUpdate> source)
    {
        var updates = new List<TurnUpdate>();
        await foreach (var u in source)
            updates.Add(u);
        return updates;
    }
}
