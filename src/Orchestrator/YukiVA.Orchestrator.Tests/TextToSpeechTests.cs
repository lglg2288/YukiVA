using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Infrastructure;
using YukiVA.Orchestrator.Infrastructure.Grpc;
using YukiVA.Orchestrator.Infrastructure.Grpc.Tts;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Tests;

public class TextToSpeechTests
{
    [Fact]
    public async Task ElevenLabs_YieldsChunksAsTheyArrive_AlignedToSamples()
    {
        var body = new GatedStream();
        var handler = new FakeHttpHandler(body);
        var tts = CreateElevenLabs(handler);

        await using var chunks = tts.SynthesizeStreamAsync("Привет!", CancellationToken.None).GetAsyncEnumerator();

        // 3 байта: целый сэмпл отдаётся сразу, третий байт ждёт пару.
        body.Push(1, 2, 3);
        Assert.True(await chunks.MoveNextAsync());
        Assert.Equal(new byte[] { 1, 2 }, chunks.Current.ToArray());

        // Второй кусок пришёл только после того, как первый уже отдан — это и есть стрим.
        body.Push(4, 5, 6);
        Assert.True(await chunks.MoveNextAsync());
        Assert.Equal(new byte[] { 3, 4, 5, 6 }, chunks.Current.ToArray());

        body.Complete();
        Assert.False(await chunks.MoveNextAsync());

        var request = handler.Request!;
        Assert.Equal("/v1/text-to-speech/voice-1/stream", request.RequestUri!.AbsolutePath);
        Assert.Equal("?output_format=pcm_24000", request.RequestUri.Query);
        Assert.Equal("test-key", request.Headers.GetValues("xi-api-key").Single());

        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("Привет!", json.RootElement.GetProperty("text").GetString());
        Assert.Equal("eleven_flash_v2_5", json.RootElement.GetProperty("model_id").GetString());
        Assert.Equal("ru", json.RootElement.GetProperty("language_code").GetString());
    }

    [Fact]
    public async Task ElevenLabs_ErrorStatus_ThrowsWithBody()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.Unauthorized, """{"detail":"invalid_api_key"}""");
        var tts = CreateElevenLabs(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            async () => { await foreach (var _ in tts.SynthesizeStreamAsync("x", CancellationToken.None)) { } });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("invalid_api_key", ex.Message);
    }

    [Fact]
    public async Task Xtts_StripsWavHeader_YieldsSinglePcmChunk()
    {
        var pcm = new byte[] { 10, 20, 30, 40 };
        var tts = new TtsGrpcClient(new FakeTtsGrpc(Wav.Write(pcm, AudioFormat.Pcm16Mono24k)));

        var chunks = new List<byte[]>();
        await foreach (var chunk in tts.SynthesizeStreamAsync("Привет", CancellationToken.None))
            chunks.Add(chunk.ToArray());

        Assert.Equal(pcm, Assert.Single(chunks));
    }

    [Fact]
    public async Task Xtts_UnexpectedFormat_Throws()
    {
        var tts = new TtsGrpcClient(new FakeTtsGrpc(Wav.Write(new byte[] { 0, 0 }, new AudioFormat(22050, 1, 16))));

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => { await foreach (var _ in tts.SynthesizeStreamAsync("x", CancellationToken.None)) { } });
    }

    [Theory]
    [InlineData("ElevenLabs", typeof(ElevenLabsTtsService))]
    [InlineData("Xtts", typeof(TtsGrpcClient))]
    [InlineData(null, typeof(ElevenLabsTtsService))]
    public void Provider_SelectsDefaultTextToSpeech(string? provider, Type expected)
    {
        using var services = BuildServices(provider);
        using var scope = services.CreateScope();

        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<ITextToSpeech>());
        Assert.IsType<TtsGrpcClient>(scope.ServiceProvider.GetRequiredKeyedService<ITextToSpeech>("Xtts"));
        Assert.IsType<ElevenLabsTtsService>(scope.ServiceProvider.GetRequiredKeyedService<ITextToSpeech>("ElevenLabs"));
    }

    [Fact]
    public void Provider_Unknown_FailsAtStartup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildServices("Silero"));
        Assert.Contains("Silero", ex.Message);
    }

    private static ServiceProvider BuildServices(string? provider)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused",
            ["Tts:Provider"] = provider
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }

    private static ElevenLabsTtsService CreateElevenLabs(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.elevenlabs.test") };
        http.DefaultRequestHeaders.Add("xi-api-key", "test-key");

        return new ElevenLabsTtsService(http, Options.Create(new TtsOptions
        {
            ElevenLabs = new ElevenLabsOptions { VoiceId = "voice-1" }
        }));
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly HttpContent _content;

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        public FakeHttpHandler(Stream body) : this(HttpStatusCode.OK, new StreamContent(body)) { }

        public FakeHttpHandler(HttpStatusCode status, string body) : this(status, new StringContent(body)) { }

        private FakeHttpHandler(HttpStatusCode status, HttpContent content)
        {
            _status = status;
            _content = content;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = _content };
        }
    }

    /// <summary>Поток, отдающий байты только когда тест их «прислал».</summary>
    private sealed class GatedStream : Stream
    {
        private readonly Channel<byte[]> _pieces = Channel.CreateUnbounded<byte[]>();

        public void Push(params byte[] bytes) => _pieces.Writer.TryWrite(bytes);
        public void Complete() => _pieces.Writer.Complete();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!await _pieces.Reader.WaitToReadAsync(cancellationToken))
                return 0;

            var piece = await _pieces.Reader.ReadAsync(cancellationToken);
            piece.CopyTo(buffer);
            return piece.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeTtsGrpc : TTSservice.TTSserviceClient
    {
        private readonly byte[] _wav;

        public FakeTtsGrpc(byte[] wav) => _wav = wav;

        public override AsyncUnaryCall<TTSresponse> GenerateAudioAsync(TTSrequest request, CallOptions options) =>
            new(Task.FromResult(new TTSresponse { Audio = ByteString.CopyFrom(_wav) }),
                Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
    }
}
