using System;
using System.Collections.Generic;
using System.Text;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Infrastructure.Grpc.Tts;

namespace YukiVA.Orchestrator.Infrastructure.Grpc;

public class TtsGrpcClient : ITextToSpeech
{
    private readonly TTSservice.TTSserviceClient _client;

    public TtsGrpcClient(TTSservice.TTSserviceClient client)
    {
        _client = client;
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new TTSrequest
        {
            Text = text
        };

        var response = await _client.GenerateAudioAsync(request, cancellationToken: cancellationToken);

        return response.Audio.ToByteArray();
    }
}
