namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Синтез речи: текст → аудио (WAV-байты).</summary>
public interface ITextToSpeech
{
    Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default);
}
