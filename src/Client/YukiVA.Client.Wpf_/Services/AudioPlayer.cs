using System.IO;
using NAudio.Wave;

namespace YukiVA.Client.Wpf.Services;

/// <summary>
/// Проигрывает WAV-ответ от сервера. Все ресурсы освобождаются сами,
/// когда воспроизведение закончится (PlaybackStopped).
/// </summary>
public sealed class AudioPlayer
{
    public void Play(byte[] wav)
    {
        var ms = new MemoryStream(wav);
        var reader = new WaveFileReader(ms);
        var output = new WaveOutEvent();

        output.PlaybackStopped += (_, _) =>
        {
            output.Dispose();
            reader.Dispose();
            ms.Dispose();
        };

        output.Init(reader);
        output.Play();
    }
}
