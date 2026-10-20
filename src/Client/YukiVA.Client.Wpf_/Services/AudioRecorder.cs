using System.IO;
using NAudio.Wave;

namespace YukiVA.Client.Wpf.Services;

/// <summary>
/// Запись звука с микрофона в формат WAV (16 кГц, 16 бит, моно) — то, что любит STT.
/// Пишем во временный файл, потому что корректный размер в WAV-заголовок
/// дописывается только при закрытии файла (Dispose писателя).
/// </summary>
public sealed class AudioRecorder
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempPath;
    private TaskCompletionSource<bool>? _stopTcs;

    public bool IsRecording { get; private set; }

    /// <summary>Начать запись с микрофона.</summary>
    public void Start()
    {
        if (IsRecording) return;

        _tempPath = Path.Combine(Path.GetTempPath(), $"yuki_{Guid.NewGuid():N}.wav");

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1) // 16000 Гц, 16 бит, 1 канал (моно)
        };
        _writer = new WaveFileWriter(_tempPath, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, a) => _writer?.Write(a.Buffer, 0, a.BytesRecorded);
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();

        IsRecording = true;
    }

    /// <summary>Остановить запись и получить готовый WAV (массив байт).</summary>
    public async Task<byte[]> StopAsync()
    {
        if (!IsRecording || _waveIn is null) return Array.Empty<byte>();

        // Останавливаем и ДОЖИДАЕМСЯ события RecordingStopped (там закрывается файл).
        _stopTcs = new TaskCompletionSource<bool>();
        _waveIn.StopRecording();
        await _stopTcs.Task;
        IsRecording = false;

        byte[] wav = await File.ReadAllBytesAsync(_tempPath!);
        File.Delete(_tempPath!);
        _tempPath = null;
        return wav;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // ВАЖНО: длина файла дописывается в WAV-заголовок именно здесь, при Dispose().
        _writer?.Dispose(); _writer = null;
        _waveIn?.Dispose(); _waveIn = null;
        _stopTcs?.TrySetResult(true);
    }
}
