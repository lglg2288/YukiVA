using System.Buffers.Binary;
using System.Text;

namespace YukiVA.Orchestrator.Application.Audio;

/// <summary>Минимальная работа с WAV (RIFF, PCM): упаковать сырой PCM и достать его обратно.</summary>
public static class Wav
{
    private const int HeaderSize = 44;

    /// <summary>PCM → WAV-файл с 44-байтным заголовком.</summary>
    public static byte[] Write(ReadOnlySpan<byte> pcm, AudioFormat format)
    {
        var wav = new byte[HeaderSize + pcm.Length];
        var span = wav.AsSpan();

        Encoding.ASCII.GetBytes("RIFF", span[0..4]);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], HeaderSize - 8 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVE", span[8..12]);

        Encoding.ASCII.GetBytes("fmt ", span[12..16]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], 16);   // размер fmt-чанка
        BinaryPrimitives.WriteInt16LittleEndian(span[20..22], 1);    // 1 = PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..24], (short)format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], format.BytesPerSecond);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..34], (short)format.BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..36], (short)format.BitsPerSample);

        Encoding.ASCII.GetBytes("data", span[36..40]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], pcm.Length);
        pcm.CopyTo(span[HeaderSize..]);

        return wav;
    }

    /// <summary>
    /// WAV-файл → формат и PCM-данные. Чанки идут в любом порядке, лишние (LIST и т.п.) пропускаются.
    /// </summary>
    public static (AudioFormat Format, ReadOnlyMemory<byte> Pcm) Read(ReadOnlyMemory<byte> wav)
    {
        var span = wav.Span;
        if (span.Length < 12 || !span[0..4].SequenceEqual("RIFF"u8) || !span[8..12].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Это не WAV: нет заголовка RIFF/WAVE.");

        AudioFormat? format = null;
        var offset = 12;
        while (offset + 8 <= span.Length)
        {
            var id = span.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset + 4, 4));
            var body = offset + 8;

            if (id.SequenceEqual("fmt "u8))
            {
                var encoding = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body, 2));
                if (encoding != 1)
                    throw new InvalidDataException($"WAV в кодировке {encoding}, поддерживается только PCM (1).");

                format = new AudioFormat(
                    SampleRate: BinaryPrimitives.ReadInt32LittleEndian(span.Slice(body + 4, 4)),
                    Channels: BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 2, 2)),
                    BitsPerSample: BinaryPrimitives.ReadInt16LittleEndian(span.Slice(body + 14, 2)));
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (format is null)
                    throw new InvalidDataException("В WAV чанк data идёт раньше fmt.");

                // Потоковые писатели иногда ставят размер 0/-1 — тогда данные идут до конца файла.
                var length = size <= 0 || body + size > span.Length ? span.Length - body : size;
                return (format, wav.Slice(body, length));
            }

            offset = body + size + (size & 1); // чанки выровнены по 2 байтам
        }

        throw new InvalidDataException("В WAV нет чанка data.");
    }
}
