using NAudio.Wave;
using YukiVA.Client.Core.Audio;

namespace YukiVA.Client.Wpf.Audio;

/// <summary>
/// Потоковое воспроизведение: куски PCM складываются в буфер, звук стартует с первого куска.
/// Считает реально прозвучавшие байты — по ним подсвечивается текущее предложение.
/// </summary>
public sealed class NAudioOutput : IAudioOutput, IDisposable
{
    private static readonly WaveFormat Format =
        new(IAudioOutput.SampleRate, IAudioOutput.BitsPerSample, IAudioOutput.Channels);

    private readonly BufferedWaveProvider _buffer = new(Format)
    {
        BufferDuration = TimeSpan.FromSeconds(120),
        DiscardOnBufferOverflow = true
    };
    private readonly SilenceCountingProvider _counting;
    private WaveOutEvent? _output;

    public NAudioOutput() => _counting = new SilenceCountingProvider(_buffer);

    public long WrittenBytes { get; private set; }

    public long PlayedBytes
    {
        get
        {
            if (_output is null) return 0;
            // Позиция устройства включает тишину, которую буфер подставлял, пока ждал следующий кусок.
            var played = _output.GetPosition() - _counting.SilenceBytes;
            return Math.Clamp(played, 0, WrittenBytes);
        }
    }

    public void Write(ReadOnlySpan<byte> pcm)
    {
        _buffer.AddSamples(pcm.ToArray(), 0, pcm.Length);
        WrittenBytes += pcm.Length;

        if (_output is null)
        {
            _output = new WaveOutEvent { DesiredLatency = 150 };
            _output.Init(_counting);
            _output.Play();
        }
    }

    public void Reset()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _buffer.ClearBuffer();
        _counting.Reset();
        WrittenBytes = 0;
    }

    public void Dispose() => Reset();

    /// <summary>Пропускает звук из буфера и считает байты тишины, подставленной при пустом буфере.</summary>
    private sealed class SilenceCountingProvider(BufferedWaveProvider source) : IWaveProvider
    {
        private long _silence;

        public WaveFormat WaveFormat => source.WaveFormat;
        public long SilenceBytes => Interlocked.Read(ref _silence);

        public int Read(byte[] buffer, int offset, int count)
        {
            var available = source.BufferedBytes;
            var read = source.Read(buffer, offset, count); // при нехватке данных дополняет нулями
            if (read > available)
                Interlocked.Add(ref _silence, read - available);
            return read;
        }

        public void Reset() => Interlocked.Exchange(ref _silence, 0);
    }
}
