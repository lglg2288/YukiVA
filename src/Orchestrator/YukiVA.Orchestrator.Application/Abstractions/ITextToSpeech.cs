using YukiVA.Orchestrator.Application.Audio;

namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Синтез речи: текст → поток аудио.</summary>
public interface ITextToSpeech
{
    /// <summary>
    /// Куски аудио по мере синтеза. Формат всегда <see cref="AudioFormat.Pcm16Mono24k"/>
    /// (без заголовка), длина куска кратна <see cref="AudioFormat.BlockAlign"/> — адаптер приводит сам.
    /// </summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(string text, CancellationToken ct);
}
