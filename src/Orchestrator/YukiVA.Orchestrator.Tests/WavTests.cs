using System.Text;
using YukiVA.Orchestrator.Application.Audio;

namespace YukiVA.Orchestrator.Tests;

public class WavTests
{
    [Fact]
    public void WriteThenRead_RoundTripsFormatAndPcm()
    {
        var pcm = new byte[] { 1, 2, 3, 4, 5, 6 };

        var wav = Wav.Write(pcm, AudioFormat.Pcm16Mono24k);
        var (format, data) = Wav.Read(wav);

        Assert.Equal(44 + pcm.Length, wav.Length);
        Assert.Equal(AudioFormat.Pcm16Mono24k, format);
        Assert.Equal(pcm, data.ToArray());
    }

    [Fact]
    public void Read_SkipsUnknownChunksBeforeData()
    {
        var wav = Wav.Write(new byte[] { 9, 9 }, new AudioFormat(22050, 2, 16));

        // Вставляем LIST-чанк нечётной длины (3 байта + 1 байт выравнивания) между fmt и data.
        var list = Encoding.ASCII.GetBytes("LIST").Concat(BitConverter.GetBytes(3)).Concat(new byte[] { 7, 7, 7, 0 });
        var patched = wav[..36].Concat(list).Concat(wav[36..]).ToArray();

        var (format, data) = Wav.Read(patched);

        Assert.Equal(new AudioFormat(22050, 2, 16), format);
        Assert.Equal(new byte[] { 9, 9 }, data.ToArray());
    }

    [Fact]
    public void Read_NotPcm_Throws()
    {
        var wav = Wav.Write(new byte[] { 0, 0 }, AudioFormat.Pcm16Mono24k);
        wav[20] = 3; // IEEE float

        Assert.Throws<InvalidDataException>(() => Wav.Read(wav));
    }

    [Fact]
    public void Read_NotWav_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Wav.Read(new byte[] { 1, 2, 3 }));
    }
}
