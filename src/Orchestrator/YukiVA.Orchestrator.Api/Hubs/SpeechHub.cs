using Microsoft.AspNetCore.SignalR;
using YukiVA.Orchestrator.Application.UseCases.ProcessSpeechTurn;
using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Api.Hubs;

/// <summary>
/// Real-time audio hub. Clients connect via WebSocket/SSE and exchange audio chunks.
///
/// Client → Server: StartSession, SendAudio
/// Server → Client: SessionStarted, Transcript, ReceiveAudio, PipelineError
/// </summary>
public sealed class SpeechHub(
    ProcessSpeechTurnHandler speechTurnHandler,
    ISessionRepository sessionRepository,
    ILogger<SpeechHub> logger) : Hub
{
    /// <summary>Creates a new session and returns its ID to the caller.</summary>
    public async Task StartSession(string userId)
    {
        var session = await sessionRepository.CreateAsync(userId, Context.ConnectionAborted);
        logger.LogInformation("Session {SessionId} created for user '{UserId}' (conn={ConnId})",
            session.Id, userId, Context.ConnectionId);

        await Clients.Caller.SendAsync("SessionStarted", session.Id.ToString(),
            cancellationToken: Context.ConnectionAborted);
    }

    /// <summary>
    /// Receives a single audio turn from the client, runs the full pipeline,
    /// and streams the result back.
    /// </summary>
    /// <param name="audioData">Raw audio bytes.</param>
    /// <param name="sessionId">Session GUID returned by StartSession.</param>
    /// <param name="format">Audio container: "wav", "mp3", "opus".</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    public async Task SendAudio(byte[] audioData, string sessionId, string format = "wav", int sampleRate = 16000)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
        {
            await Clients.Caller.SendAsync("PipelineError", "Invalid session ID.");
            return;
        }

        try
        {
            var inputAudio = new AudioChunk(audioData, format, sampleRate);
            var command = new ProcessSpeechTurnCommand(sessionGuid, inputAudio);

            var result = await speechTurnHandler.HandleAsync(command, Context.ConnectionAborted);

            // Send transcript so the client can display it.
            await Clients.Caller.SendAsync("Transcript", result.TranscribedText, result.AssistantText,
                cancellationToken: Context.ConnectionAborted);

            // Send synthesized audio back.
            await Clients.Caller.SendAsync("ReceiveAudio", result.OutputAudio.Data, result.OutputAudio.Format,
                cancellationToken: Context.ConnectionAborted);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Audio processing cancelled for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Pipeline error for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("PipelineError", "Failed to process audio. Please try again.");
        }
    }

    /// <summary>Closes and removes a session.</summary>
    public async Task EndSession(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
            return;

        await sessionRepository.DeleteAsync(sessionGuid, Context.ConnectionAborted);
        logger.LogInformation("Session {SessionId} closed (conn={ConnId})", sessionId, Context.ConnectionId);
    }
}
