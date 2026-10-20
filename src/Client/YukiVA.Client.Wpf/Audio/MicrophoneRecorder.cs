using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace YukiVA.Client.Wpf.Audio;

/// <summary>Запись с микрофона в WAV 16 кГц / 16 бит / моно — формат, который ждёт STT.</summary>
public sealed class MicrophoneRecorder
{
    private static readonly WaveFormat Format = new(16000, 16, 1);

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private MemoryStream? _buffer;
    private TaskCompletionSource? _stopped;

    public bool IsRecording => _waveIn is not null;

    public void Start()
    {
        if (IsRecording) return;

        _buffer = new MemoryStream();
        // IgnoreDisposeStream: writer при Dispose дописывает размеры в заголовок, но не закрывает поток.
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), Format);
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _waveIn = new WaveInEvent { WaveFormat = Format, BufferMilliseconds = 50 };
        _waveIn.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _waveIn.RecordingStopped += (_, _) =>
        {
            _writer?.Dispose();
            _writer = null;
            _stopped?.TrySetResult();
        };
        _waveIn.StartRecording();
    }

    /// <summary>Остановить запись: готовый WAV и длительность речи.</summary>
    public async Task<(byte[] Wav, TimeSpan Duration)> StopAsync()
    {
        if (_waveIn is null || _stopped is null || _buffer is null)
            return (Array.Empty<byte>(), TimeSpan.Zero);

        _waveIn.StopRecording();
        await _stopped.Task; // заголовок WAV готов только после RecordingStopped

        _waveIn.Dispose();
        _waveIn = null;

        var wav = _buffer.ToArray();
        _buffer = null;
        var duration = TimeSpan.FromSeconds((double)Math.Max(0, wav.Length - 44) / Format.AverageBytesPerSecond);
        return (wav, duration);
    }
}
