using System.Text;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Application.Models;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

public class ProcessVoiceTurnHandler
{
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

    /// <summary>Голосовой ход: аудио → STT → LLM → (текст|tool_call).</summary>
    public async Task<ProcessVoiceTurnResult> HandleVoiceAsync(
        byte[] audio,
        Guid? sessionPublicId,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct = default)
    {
        var session = await GetOrCreateSessionAsync(sessionPublicId, ct);

        var userText = await _stt.TranscribeAsync(audio, ct);
        if (string.IsNullOrWhiteSpace(userText))
        {
            var audioEmpty = await SynthesizeWavAsync(
                "Не удалось распознать речь. Попробуйте ещё раз.", ct);
            return ProcessVoiceTurnResult.Audio(
                session.PublicId, "", "Не удалось распознать речь.", audioEmpty);
        }

        session.AddUserMessage(userText);

        return await CompleteAndBuildResultAsync(session, userText, availableTools, ct);
    }

    /// <summary>Продолжение после исполнения инструмента клиентом.</summary>
    public async Task<ProcessVoiceTurnResult> HandleToolResultAsync(
        Guid sessionPublicId,
        string? toolCallId,
        string toolResult,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByPublicIdAsync(sessionPublicId, ct)
            ?? throw new InvalidOperationException($"Сессия {sessionPublicId} не найдена.");

        // Клиент пока присылает один результат; без id — отвечаем на единственный висящий вызов.
        // Несколько результатов за раз — шаг 5 (POST /api/voice/tool-results).
        toolCallId ??= session.PendingToolCalls.Count == 1
            ? session.PendingToolCalls[0].CallId
            : throw new InvalidOperationException("Не указан toolCallId, а висящих вызовов не один.");

        session.AddToolResult(toolCallId, toolResult);

        // Пока ответ клиенту — один вызов: следующие висящие отдаём по одному, LLM не спрашиваем.
        if (session.PendingToolCalls.Count > 0)
        {
            await _sessions.SaveChangesAsync(ct);
            return ToolCallResult(session, session.PendingToolCalls[0]);
        }

        return await CompleteAndBuildResultAsync(session, userText: null, availableTools, ct);
    }

    // Общая часть: спросить LLM и собрать результат (текст→TTS либо tool_call).
    private async Task<ProcessVoiceTurnResult> CompleteAndBuildResultAsync(
        ConversationSession session,
        string? userText,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct)
    {
        // Шаг 4 заменит это потоковым конвейером; пока собираем ответ целиком.
        var text = new StringBuilder();
        IReadOnlyList<ToolCall> toolCalls = Array.Empty<ToolCall>();
        await foreach (var e in _llm.StreamAsync(session.Messages, availableTools, ct))
        {
            switch (e)
            {
                case LlmTextDelta delta: text.Append(delta.Text); break;
                case LlmToolCalls calls: toolCalls = calls.Calls; break;
            }
        }

        var assistantText = text.ToString();
        session.AddAssistantMessage(assistantText, toolCalls);
        await _sessions.SaveChangesAsync(ct);

        if (toolCalls.Count > 0)
            return ToolCallResult(session, toolCalls[0]);

        var audioReply = await SynthesizeWavAsync(assistantText, ct);
        return ProcessVoiceTurnResult.Audio(
            session.PublicId, userText ?? "", assistantText, audioReply);
    }

    // Шаг 5 заменит ответ audio/wav потоком событий; пока склеиваем PCM в один WAV.
    private async Task<byte[]> SynthesizeWavAsync(string text, CancellationToken ct)
    {
        using var pcm = new MemoryStream();
        await foreach (var chunk in _tts.SynthesizeStreamAsync(text, ct))
            pcm.Write(chunk.Span);

        return Wav.Write(pcm.GetBuffer().AsSpan(0, (int)pcm.Length), AudioFormat.Pcm16Mono24k);
    }

    private static ProcessVoiceTurnResult ToolCallResult(ConversationSession session, ToolCall call) =>
        ProcessVoiceTurnResult.ToolCall(session.PublicId, call.CallId, call.Name, call.ArgumentsJson);

    private async Task<ConversationSession> GetOrCreateSessionAsync(
        Guid? sessionPublicId, CancellationToken ct)
    {
        if (sessionPublicId is Guid id)
        {
            return await _sessions.GetByPublicIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Сессия {id} не найдена.");
        }
        var session = new ConversationSession();
        await _sessions.AddAsync(session, ct);
        return session;
    }
}
