using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

/// <summary>
/// Голосовой ход потоком: STT → LLM (дельты текста) → предложения → TTS (куски аудио).
/// Текст и звук отдаются по мере готовности, звук начинается после первого предложения.
/// </summary>
public class ProcessVoiceTurnHandler
{
    private const string NotRecognizedReply = "Не удалось распознать речь. Попробуйте ещё раз.";

    private readonly ISpeechToText _stt;
    private readonly ILlmService _llm;
    private readonly ITextToSpeech _tts;
    private readonly ISessionRepository _sessions;

    public ProcessVoiceTurnHandler(
        ISpeechToText stt, ILlmService llm, ITextToSpeech tts, ISessionRepository sessions)
    {
        _stt = stt;
        _llm = llm;
        _tts = tts;
        _sessions = sessions;
    }

    /// <summary>Голосовой ход: аудио → распознанный текст → потоковый ответ.</summary>
    public async IAsyncEnumerable<TurnEvent> HandleVoiceAsync(
        byte[] audio,
        Guid? sessionPublicId,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await GetOrCreateSessionAsync(sessionPublicId, ct);
        yield return new SessionStarted(session.PublicId);

        var userText = await _stt.TranscribeAsync(audio, ct);
        yield return new UserTranscript(userText);

        if (string.IsNullOrWhiteSpace(userText))
        {
            // В историю не пишем: пользователь ничего не сказал.
            await foreach (var e in SpeakFixedReplyAsync(NotRecognizedReply, ct))
                yield return e;
            yield return new TurnCompleted();
            yield break;
        }

        session.AddUserMessage(userText);
        await _sessions.SaveChangesAsync(ct);

        await foreach (var e in RunAssistantTurnAsync(session, tools, ct))
            yield return e;
    }

    /// <summary>Продолжение после того, как клиент исполнил ВСЕ запрошенные вызовы.</summary>
    public async IAsyncEnumerable<TurnEvent> HandleToolResultsAsync(
        Guid sessionPublicId,
        IReadOnlyList<ToolResult> results,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await _sessions.GetByPublicIdAsync(sessionPublicId, ct)
            ?? throw new InvalidOperationException($"Сессия {sessionPublicId} не найдена.");

        foreach (var result in results)
            session.AddToolResult(result.ToolCallId, result.Content);

        if (session.PendingToolCalls.Count > 0)
            throw new InvalidOperationException(
                "Нет результатов для вызовов: " + string.Join(", ", session.PendingToolCalls.Select(c => c.CallId)));

        await _sessions.SaveChangesAsync(ct);
        yield return new SessionStarted(session.PublicId);

        await foreach (var e in RunAssistantTurnAsync(session, tools, ct))
            yield return e;
    }

    /// <summary>
    /// Конвейер ответа. Две фоновые задачи пишут в общий канал событий:
    /// LLM — дельты текста (и предложения в канал для TTS), TTS — куски аудио строго по порядку.
    /// Этот метод читает канал и отдаёт события; DbContext трогает только он.
    /// </summary>
    private async IAsyncEnumerable<TurnEvent> RunAssistantTurnAsync(
        ConversationSession session,
        IReadOnlyList<ToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var events = Channel.CreateUnbounded<TurnEvent>(new UnboundedChannelOptions { SingleReader = true });
        var sentences = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // Снимок истории: фоновая задача не должна читать коллекцию, которую меняет этот поток.
        var history = session.Messages.ToList();

        var pipeline = RunPipelineAsync(
            ProduceAsync(history, tools, sentences.Writer, events.Writer, cts.Token),
            SpeakAsync(sentences.Reader, events.Writer, cts.Token),
            events.Writer, cts);

        IReadOnlyList<ToolCall> toolCalls = Array.Empty<ToolCall>();
        try
        {
            await foreach (var e in events.Reader.ReadAllAsync(ct))
            {
                if (e is AnswerReady answer)
                {
                    // LLM закончила — сохраняем ответ, не дожидаясь конца озвучки.
                    toolCalls = answer.ToolCalls;
                    if (!string.IsNullOrWhiteSpace(answer.Text) || toolCalls.Count > 0)
                    {
                        session.AddAssistantMessage(answer.Text, toolCalls);
                        await _sessions.SaveChangesAsync(ct);
                    }
                    continue;
                }

                yield return e;
            }

            await pipeline; // пробросить ошибку LLM/TTS, если была
        }
        finally
        {
            // Клиент ушёл или ошибка — останавливаем LLM и TTS и ждём, чтобы ничего не висело.
            cts.Cancel();
            try { await pipeline; } catch { /* уже обработано выше или ход отменён */ }
        }

        // Текст перед вызовами («Сейчас посмотрю») уже озвучен — теперь просим клиента исполнить их.
        if (toolCalls.Count > 0)
            yield return new ToolCallsRequested(toolCalls);

        yield return new TurnCompleted();
    }

    private static async Task RunPipelineAsync(
        Task produce, Task speak, ChannelWriter<TurnEvent> events, CancellationTokenSource cts)
    {
        try
        {
            // Упала одна задача — гасим вторую, чтобы не генерировать и не озвучивать впустую.
            await Task.WhenAll(CancelOnFault(produce), CancelOnFault(speak));
            events.TryComplete();
        }
        catch (Exception ex)
        {
            events.TryComplete(ex);
            throw;
        }

        async Task CancelOnFault(Task task)
        {
            try { await task; }
            catch { cts.Cancel(); throw; }
        }
    }

    /// <summary>LLM → дельты текста в события, готовые предложения в канал для TTS.</summary>
    private async Task ProduceAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> tools,
        ChannelWriter<string> sentences,
        ChannelWriter<TurnEvent> events,
        CancellationToken ct)
    {
        try
        {
            var splitter = new SentenceSplitter();
            var text = new StringBuilder();
            IReadOnlyList<ToolCall> toolCalls = Array.Empty<ToolCall>();

            await foreach (var e in _llm.StreamAsync(history, tools, ct))
            {
                switch (e)
                {
                    case LlmTextDelta delta:
                        text.Append(delta.Text);
                        await events.WriteAsync(new AssistantTextDelta(delta.Text), ct);
                        foreach (var sentence in splitter.Append(delta.Text))
                            await sentences.WriteAsync(sentence, ct);
                        break;

                    case LlmToolCalls calls:
                        toolCalls = calls.Calls;
                        break;
                }
            }

            if (splitter.Flush() is { } rest)
                await sentences.WriteAsync(rest, ct);

            sentences.TryComplete();
            await events.WriteAsync(new AnswerReady(text.ToString(), toolCalls), ct);
        }
        catch (Exception ex)
        {
            sentences.TryComplete(ex);
            throw;
        }
    }

    /// <summary>Предложения → TTS последовательно (порядок звука важен) → куски аудио в события.</summary>
    private async Task SpeakAsync(ChannelReader<string> sentences, ChannelWriter<TurnEvent> events, CancellationToken ct)
    {
        var seq = 0;
        await foreach (var sentence in sentences.ReadAllAsync(ct))
        {
            await foreach (var pcm in _tts.SynthesizeStreamAsync(sentence, ct))
                await events.WriteAsync(new AudioChunk(seq++, pcm), ct);
        }
    }

    private async IAsyncEnumerable<TurnEvent> SpeakFixedReplyAsync(
        string text, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new AssistantTextDelta(text);

        var seq = 0;
        await foreach (var pcm in _tts.SynthesizeStreamAsync(text, ct))
            yield return new AudioChunk(seq++, pcm);
    }

    private async Task<ConversationSession> GetOrCreateSessionAsync(
        Guid? sessionPublicId, CancellationToken ct)
    {
        if (sessionPublicId is Guid id)
        {
            return await _sessions.GetByPublicIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Сессия {id} не найдена.");
        }
        // Сохраняем сразу: клиент получит sessionId первым событием и может прийти с ним,
        // даже если ход оборвётся до первого сообщения (пустая речь, ошибка STT).
        var session = new ConversationSession();
        await _sessions.AddAsync(session, ct);
        await _sessions.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>Внутренний сигнал из задачи LLM: ответ готов, его пора сохранить. Наружу не уходит.</summary>
    private sealed record AnswerReady(string Text, IReadOnlyList<ToolCall> ToolCalls) : TurnEvent;
}
