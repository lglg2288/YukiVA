namespace YukiVA.Orchestrator.Application.Audio;

/// <summary>Формат несжатого PCM-аудио (little-endian, со знаком).</summary>
public sealed record AudioFormat(int SampleRate, int Channels, int BitsPerSample)
{
    /// <summary>
    /// Единственный формат синтезированной речи во всей системе: PCM s16le, моно, 24 кГц.
    /// Адаптеры TTS приводят аудио к нему сами; клиент получает его в событии audioFormat.
    /// </summary>
    public static AudioFormat Pcm16Mono24k { get; } = new(24000, 1, 16);

    /// <summary>Байт на один сэмпл всех каналов — куски аудио должны быть кратны ему.</summary>
    public int BlockAlign => Channels * BitsPerSample / 8;

    public int BytesPerSecond => SampleRate * BlockAlign;
}
