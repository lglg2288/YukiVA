using Microsoft.Extensions.Logging;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessSpeechTurn;

/// <summary>
/// Orchestrates a single speech-to-speech turn:
/// AudioChunk → STT → LLM (tool-call loop) → TTS → AudioChunk
/// </summary>
public sealed class ProcessSpeechTurnHandler(
    ISttService sttService,
    ILlmService llmService,
    ITtsService ttsService,
    IMcpToolProvider mcpToolProvider,
    ISessionRepository sessionRepository,
    ILogger<ProcessSpeechTurnHandler> logger)
{
    public async Task<ProcessSpeechTurnResult> HandleAsync(
        ProcessSpeechTurnCommand command,
        CancellationToken ct = default)
    {
        var session = await sessionRepository.GetAsync(command.SessionId, ct)
            ?? throw new InvalidOperationException($"Session {command.SessionId} not found.");

        session.SetProcessing();

        try
        {
            return await RunPipelineAsync(session, command.InputAudio, ct);
        }
        finally
        {
            session.SetActive();
            await sessionRepository.UpdateAsync(session, ct);
        }
    }

    private async Task<ProcessSpeechTurnResult> RunPipelineAsync(
        AudioSession session,
        AudioChunk inputAudio,
        CancellationToken ct)
    {
        // ── 1. STT ────────────────────────────────────────────────────────────
        logger.LogInformation("[{SessionId}] STT: transcribing {Bytes} bytes ({Format})",
            session.Id, inputAudio.Data.Length, inputAudio.Format);

        var transcript = await sttService.TranscribeAsync(inputAudio, ct);

        logger.LogInformation("[{SessionId}] STT result: \"{Text}\" (conf={Confidence:P0})",
            session.Id, transcript.Text, transcript.Confidence);

        session.AddMessage(ConversationMessage.FromUser(session.Id, transcript.Text));

        // ── 2. LLM + MCP tool-call loop ───────────────────────────────────────
        var tools = mcpToolProvider.GetAvailableTools();
        string assistantText;

        while (true)
        {
            logger.LogDebug("[{SessionId}] LLM: generating with {MsgCount} messages, {ToolCount} tools",
                session.Id, session.Messages.Count, tools.Count);

            var llmResult = await llmService.GenerateAsync(session.Messages, tools, ct);

            if (!llmResult.HasToolCalls)
            {
                assistantText = llmResult.Text;
                session.AddMessage(ConversationMessage.FromAssistant(session.Id, assistantText));
                break;
            }

            logger.LogInformation("[{SessionId}] LLM requested {Count} tool call(s)",
                session.Id, llmResult.ToolCalls.Count);

            foreach (var toolCall in llmResult.ToolCalls)
            {
                var toolResult = await mcpToolProvider.ExecuteToolAsync(
                    toolCall.ToolName, toolCall.ArgumentsJson, ct);

                logger.LogDebug("[{SessionId}] Tool '{Tool}' returned: {Result}",
                    session.Id, toolCall.ToolName, toolResult);

                session.AddMessage(
                    ConversationMessage.FromToolResult(session.Id, toolCall.CallId, toolResult));
            }
        }

        logger.LogInformation("[{SessionId}] LLM response: \"{Text}\"",
            session.Id, assistantText);

        // ── 3. TTS ────────────────────────────────────────────────────────────
        logger.LogDebug("[{SessionId}] TTS: synthesizing {Chars} chars", session.Id, assistantText.Length);

        var outputAudio = await ttsService.SynthesizeAsync(assistantText, new SpeechOptions(), ct);

        logger.LogInformation("[{SessionId}] TTS done: {Bytes} bytes ({Format})",
            session.Id, outputAudio.Data.Length, outputAudio.Format);

        return new ProcessSpeechTurnResult(outputAudio, transcript.Text, assistantText);
    }
}
