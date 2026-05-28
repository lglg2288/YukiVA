using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Application.Abstractions;

public interface IPipelineOrchestrator
{
    /// <summary>
    /// Runs the full speech-to-speech pipeline for a single conversation turn.
    /// Audio → STT → LLM (with tool call loop) → TTS → Audio.
    /// </summary>
    Task<AudioChunk> ProcessTurnAsync(Guid sessionId, AudioChunk inputAudio, CancellationToken ct = default);
}
