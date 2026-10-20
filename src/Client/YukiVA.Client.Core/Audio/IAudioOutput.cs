namespace YukiVA.Client.Core.Audio;

/// <summary>
/// Потоковый вывод звука: PCM s16le, моно, 24 кГц — формат, который отдаёт сервер.
/// Счётчики в байтах этого формата нужны, чтобы понять, какое предложение звучит сейчас.
/// </summary>
public interface IAudioOutput
{
    public const int SampleRate = 24000;
    public const int Channels = 1;
    public const int BitsPerSample = 16;

    /// <summary>Сколько байт передано на воспроизведение с начала хода.</summary>
    long WrittenBytes { get; }

    /// <summary>Сколько из них уже прозвучало (без тишины при опустевшем буфере).</summary>
    long PlayedBytes { get; }

    /// <summary>Добавить кусок в очередь; воспроизведение стартует с первым куском.</summary>
    void Write(ReadOnlySpan<byte> pcm);

    /// <summary>Остановить звук, очистить очередь и обнулить счётчики (новый ход или перебивание).</summary>
    void Reset();
}
