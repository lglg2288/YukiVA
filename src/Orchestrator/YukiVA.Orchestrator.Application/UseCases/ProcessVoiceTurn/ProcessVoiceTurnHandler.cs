using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;
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
            var audioEmpty = await _tts.SynthesizeAsync(
                "Не удалось распознать речь. Попробуйте ещё раз.", ct);
            return ProcessVoiceTurnResult.Audio(
                session.PublicId, "", "Не удалось распознать речь.", audioEmpty);
        }

        session.AddMessage(MessageRole.User, userText);

        return await CompleteAndBuildResultAsync(session, userText, availableTools, ct);
    }

    /// <summary>Продолжение после исполнения инструмента клиентом.</summary>
    public async Task<ProcessVoiceTurnResult> HandleToolResultAsync(
        Guid sessionPublicId,
        string toolResult,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct = default)
    {
        var session = await _sessions.GetByPublicIdAsync(sessionPublicId, ct)
            ?? throw new InvalidOperationException($"Сессия {sessionPublicId} не найдена.");

        // результат инструмента кладём в историю как сообщение роли Tool
        session.AddMessage(MessageRole.Tool, toolResult);

        return await CompleteAndBuildResultAsync(session, userText: null, availableTools, ct);
    }

    // Общая часть: спросить LLM и собрать результат (текст→TTS либо tool_call).
    private async Task<ProcessVoiceTurnResult> CompleteAndBuildResultAsync(
        ConversationSession session,
        string? userText,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken ct)
    {
        var llmResult = await _llm.CompleteAsync(session.Messages, availableTools, ct);

        if (llmResult.IsToolCall)
        {
            session.AddMessage(MessageRole.Assistant,
                $"[tool_call:{llmResult.ToolName}] {llmResult.ToolArgumentsJson}");
            await _sessions.SaveChangesAsync(ct);

            return ProcessVoiceTurnResult.ToolCall(
                session.PublicId,
                llmResult.ToolCallId!,
                llmResult.ToolName!,
                llmResult.ToolArgumentsJson!);
        }

        var assistantText = llmResult.Text ?? "";
        session.AddMessage(MessageRole.Assistant, assistantText);
        await _sessions.SaveChangesAsync(ct);

        var audioReply = await _tts.SynthesizeAsync(assistantText, ct);
        return ProcessVoiceTurnResult.Audio(
            session.PublicId, userText ?? "", assistantText, audioReply);
    }

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
