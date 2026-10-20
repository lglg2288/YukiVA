using System.Runtime.CompilerServices;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Infrastructure.Grpc.Tts;

namespace YukiVA.Orchestrator.Infrastructure.Grpc;

/// <summary>
/// Локальный XTTS по gRPC. Сервис отдаёт WAV целиком, поэтому «поток» — один кусок.
/// Потоковый inference_stream — Фаза 2.
/// </summary>
public class TtsGrpcClient : ITextToSpeech
{
    private readonly TTSservice.TTSserviceClient _client;

    public TtsGrpcClient(TTSservice.TTSserviceClient client)
    {
        _client = client;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var response = await _client.GenerateAudioAsync(new TTSrequest { Text = text }, cancellationToken: ct);

        var (format, pcm) = Wav.Read(response.Audio.Memory);

        // XTTS пишет PCM_16 24 кГц моно. Если сервис поменяют — здесь нужен ресемплинг, а не тихий шум у клиента.
        if (format != AudioFormat.Pcm16Mono24k)
            throw new InvalidDataException(
                $"XTTS вернул {format}, ожидался {AudioFormat.Pcm16Mono24k}.");

        yield return pcm;
    }
}
