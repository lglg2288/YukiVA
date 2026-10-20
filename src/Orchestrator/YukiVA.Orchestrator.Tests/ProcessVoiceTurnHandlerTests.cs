using System.Runtime.CompilerServices;
using System.Text;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Tests;

public class ProcessVoiceTurnHandlerTests
{
    private static readonly ToolDefinition[] Tools = { new("query", "SQL", """{"type":"object"}""") };

    private readonly FakeSessions _sessions = new();
    private readonly FakeStt _stt = new();
    private readonly FakeLlm _llm = new();
    private readonly FakeTts _tts = new();

    private ProcessVoiceTurnHandler Handler => new(_stt, _llm, _tts, _sessions);

    [Fact]
    public async Task TextTurn_EventsInOrder_AudioFollowsSentences()
    {
        _stt.Text = "расскажи о себе";
        _llm.Turns.Enqueue(Text("Меня зовут Юки, я ассистент. ", "Помогаю голосом и текстом."));

        var events = await CollectAsync(Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools));

        var session = _sessions.Single;
        Assert.Equal(new SessionStarted(session.PublicId), events[0]);
        Assert.Equal(new UserTranscript("расскажи о себе"), events[1]);
        Assert.IsType<TurnCompleted>(events[^1]);

        Assert.Equal("Меня зовут Юки, я ассистент. Помогаю голосом и текстом.",
            string.Concat(events.OfType<AssistantTextDelta>().Select(d => d.Text)));

        // Каждое предложение — два куска TTS, по порядку, seq сквозной.
        var audio = events.OfType<AudioChunk>().ToList();
        Assert.Equal(Enumerable.Range(0, 4), audio.Select(a => a.Seq));
        Assert.Equal(
            new[] { "Меня зовут Юки, я ассистент.#0", "Меня зовут Юки, я ассистент.#1", "Помогаю голосом и текстом.#0", "Помогаю голосом и текстом.#1" },
            audio.Select(a => Encoding.UTF8.GetString(a.Pcm.Span)));

        Assert.Equal(new[] { MessageRole.User, MessageRole.Assistant }, session.Messages.Select(m => m.Role));
        Assert.Equal("Меня зовут Юки, я ассистент. Помогаю голосом и текстом.", session.Messages[1].Text);
    }

    [Fact]
    public async Task AudioStartsBeforeLlmFinishes()
    {
        _stt.Text = "привет";
        var gate = new TaskCompletionSource();
        _llm.Turns.Enqueue(Gated(gate.Task, "Первое предложение готово. ", "Второе после паузы."));

        var events = new List<TurnEvent>();
        await foreach (var e in Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools))
        {
            events.Add(e);
            // LLM застряла после первого предложения, а звук уже пришёл — отпускаем её.
            if (e is AudioChunk) gate.TrySetResult();
        }

        var firstAudio = events.FindIndex(e => e is AudioChunk);
        var lastDelta = events.FindLastIndex(e => e is AssistantTextDelta);
        Assert.True(firstAudio >= 0 && firstAudio < lastDelta, "Звук должен начаться до конца текста.");
        Assert.IsType<TurnCompleted>(events[^1]);
    }

    [Fact]
    public async Task TwoToolCalls_PrefaceVoiced_ThenCallsRequested_ThenResultsContinueTurn()
    {
        _stt.Text = "сколько пользователей и заказов?";
        var calls = new[]
        {
            new ToolCall("call_a", "query", """{"sql":"select count(*) from users"}"""),
            new ToolCall("call_b", "query", """{"sql":"select count(*) from orders"}""")
        };
        _llm.Turns.Enqueue(Text("Сейчас посмотрю в базе данных.").Append(new LlmToolCalls(calls)));

        var first = await CollectAsync(Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools));

        // Озвучка вступления → вызовы → конец хода.
        Assert.IsType<TurnCompleted>(first[^1]);
        Assert.Equal(calls, Assert.IsType<ToolCallsRequested>(first[^2]).Calls);
        Assert.True(first.FindLastIndex(e => e is AudioChunk) < first.Count - 2);

        var session = _sessions.Single;
        Assert.Equal(calls, session.PendingToolCalls);

        _llm.Turns.Enqueue(Text("Пользователей сорок два, заказов семь."));
        var second = await CollectAsync(Handler.HandleToolResultsAsync(
            session.PublicId,
            new[] { new ToolResult("call_b", "7"), new ToolResult("call_a", "42") },
            Tools));

        Assert.Equal(new SessionStarted(session.PublicId), second[0]);
        Assert.DoesNotContain(second, e => e is ToolCallsRequested);
        Assert.IsType<TurnCompleted>(second[^1]);
        Assert.Equal(0, second.OfType<AudioChunk>().First().Seq);

        // LLM получила историю с обоими результатами.
        var history = _llm.Histories[1];
        Assert.Equal(new[] { "call_b", "call_a" }, history.Where(m => m.Role == MessageRole.Tool).Select(m => m.ToolCallId));
        Assert.Equal(5, session.Messages.Count);
        Assert.Empty(session.PendingToolCalls);
    }

    [Fact]
    public async Task ToolResults_NotAllCalls_Throws()
    {
        _stt.Text = "что в базе?";
        _llm.Turns.Enqueue(new LlmStreamEvent[]
        {
            new LlmToolCalls(new[] { new ToolCall("call_a", "query", "{}"), new ToolCall("call_b", "query", "{}") }),
            new LlmCompleted("tool_calls")
        });
        await CollectAsync(Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(Handler.HandleToolResultsAsync(
            _sessions.Single.PublicId, new[] { new ToolResult("call_a", "1") }, Tools)));
    }

    [Fact]
    public async Task EmptyTranscript_SpeaksFixedReply_SavesNothing()
    {
        _stt.Text = "  ";

        var events = await CollectAsync(Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools));

        Assert.Contains(events, e => e is AudioChunk);
        Assert.IsType<TurnCompleted>(events[^1]);
        Assert.Empty(_sessions.Single.Messages);
        Assert.Empty(_llm.Histories);
    }

    [Fact]
    public async Task TtsFailure_PropagatesAndStopsLlm()
    {
        _stt.Text = "привет";
        _tts.FailOn = "Первое предложение готово.";
        var gate = new TaskCompletionSource();
        _llm.Turns.Enqueue(Gated(gate.Task, "Первое предложение готово. ", "Второе никогда не придёт."));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CollectAsync(Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools)));

        // LLM ждала на gate и была отменена, а не дождалась конца.
        Assert.True(_llm.WasCancelled);
    }

    [Fact]
    public async Task ClientStopsReading_CancelsLlm()
    {
        _stt.Text = "привет";
        var gate = new TaskCompletionSource();
        _llm.Turns.Enqueue(Gated(gate.Task, "Первое предложение готово. ", "Второе."));

        await foreach (var e in Handler.HandleVoiceAsync(new byte[] { 1 }, null, Tools))
        {
            if (e is AudioChunk) break; // клиент отключился
        }

        Assert.True(_llm.WasCancelled);
    }

    private static List<LlmStreamEvent> Text(params string[] deltas) =>
        deltas.Select(d => (LlmStreamEvent)new LlmTextDelta(d)).Append(new LlmCompleted("stop")).ToList();

    private static async IAsyncEnumerable<LlmStreamEvent> Gated(
        Task gate, string first, string second, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new LlmTextDelta(first);
        // Таймаут: если звук не пришёл до конца текста, тест упадёт, а не повиснет.
        await gate.WaitAsync(TimeSpan.FromSeconds(5), ct);
        yield return new LlmTextDelta(second);
        yield return new LlmCompleted("stop");
    }

    private static async Task<List<TurnEvent>> CollectAsync(IAsyncEnumerable<TurnEvent> source)
    {
        var events = new List<TurnEvent>();
        await foreach (var e in source)
            events.Add(e);
        return events;
    }

    private sealed class FakeStt : ISpeechToText
    {
        public string Text { get; set; } = "";
        public Task<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default) =>
            Task.FromResult(Text);
    }

    private sealed class FakeLlm : ILlmService
    {
        // Каждый вызов StreamAsync берёт следующий сценарий: готовый список или IAsyncEnumerable.
        public Queue<object> Turns { get; } = new();
        public List<List<Message>> Histories { get; } = new();
        public bool WasCancelled { get; private set; }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            IReadOnlyList<Message> history, IReadOnlyList<ToolDefinition> tools,
            [EnumeratorCancellation] CancellationToken ct)
        {
            Histories.Add(history.ToList());
            var turn = Turns.Dequeue();
            var source = turn as IAsyncEnumerable<LlmStreamEvent> ?? ToAsync((IEnumerable<LlmStreamEvent>)turn);

            await using var e = source.GetAsyncEnumerator(ct);
            while (true)
            {
                try
                {
                    if (!await e.MoveNextAsync()) yield break;
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    throw;
                }
                yield return e.Current;
            }
        }

        private static async IAsyncEnumerable<LlmStreamEvent> ToAsync(IEnumerable<LlmStreamEvent> events)
        {
            foreach (var e in events)
            {
                await Task.Yield();
                yield return e;
            }
        }
    }

    /// <summary>На предложение — два куска «предложение#n», чтобы проверять порядок.</summary>
    private sealed class FakeTts : ITextToSpeech
    {
        public string? FailOn { get; set; }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
            string text, [EnumeratorCancellation] CancellationToken ct)
        {
            if (text == FailOn)
                throw new InvalidOperationException("TTS недоступен");

            for (var i = 0; i < 2; i++)
            {
                await Task.Yield();
                yield return Encoding.UTF8.GetBytes($"{text}#{i}");
            }
        }
    }

    private sealed class FakeSessions : ISessionRepository
    {
        private readonly List<ConversationSession> _sessions = new();

        public ConversationSession Single => Assert.Single(_sessions);

        public Task AddAsync(ConversationSession session, CancellationToken cancellationToken = default)
        {
            _sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<ConversationSession?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.FirstOrDefault(s => s.PublicId == publicId));

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
