using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Domain.Interfaces;

public interface ITtsService
{
    Task<AudioChunk> SynthesizeAsync(string text, SpeechOptions options, CancellationToken ct = default);
}
