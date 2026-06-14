namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Распознавание речи: аудио → текст.</summary>
public interface ISpeechToText
{
    Task<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default);
}
