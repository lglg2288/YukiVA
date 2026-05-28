using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Domain.Interfaces;

public interface ISttService
{
    Task<TranscriptResult> TranscribeAsync(AudioChunk audio, CancellationToken ct = default);
}
