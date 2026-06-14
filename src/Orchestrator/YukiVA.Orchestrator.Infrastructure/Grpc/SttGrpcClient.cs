using Google.Protobuf;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Infrastructure.Grpc.Stt;

namespace YukiVA.Orchestrator.Infrastructure.Grpc;

public class SttGrpcClient : ISpeechToText
{
    private readonly STTservice.STTserviceClient _client;

    public SttGrpcClient(STTservice.STTserviceClient client)
    {
        _client = client;
    }
    public async Task<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default)
    {
        var request = new STTrequest
        {
            Audio = ByteString.CopyFrom(audio)
        };

        var response = await _client.ConvertToTextAsync(request, cancellationToken: cancellationToken);

        return response.Text;
    }
}
