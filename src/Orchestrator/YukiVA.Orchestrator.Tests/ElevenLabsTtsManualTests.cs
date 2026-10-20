using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Tests;

/// <summary>
/// Прогон против настоящего ElevenLabs: пишет out.wav, его нужно послушать.
/// Нужны Tts:ElevenLabs:ApiKey и Tts:ElevenLabs:VoiceId в user-secrets проекта Api
/// (или Tts__ElevenLabs__ApiKey / Tts__ElevenLabs__VoiceId в окружении); без них тест ничего не делает.
/// Запуск: dotnet test --filter Category=Manual --logger "console;verbosity=detailed"
/// </summary>
[Trait("Category", "Manual")]
public class ElevenLabsTtsManualTests
{
    private const string ApiUserSecretsId = "f2ca71cb-aea5-43f0-b6a0-21320c95bac4";

    private readonly ITestOutputHelper _output;

    public ElevenLabsTtsManualTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RealApi_StreamsPcm_WritesOutWav()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(ApiUserSecretsId)
            .AddEnvironmentVariables()
            .Build();

        var options = config.GetSection(TtsOptions.SectionName).Get<TtsOptions>() ?? new TtsOptions();
        if (string.IsNullOrWhiteSpace(options.ElevenLabs.ApiKey) || string.IsNullOrWhiteSpace(options.ElevenLabs.VoiceId))
        {
            _output.WriteLine("Tts:ElevenLabs:ApiKey или VoiceId не заданы — пропускаем.");
            return;
        }

        var http = new HttpClient { BaseAddress = new Uri(options.ElevenLabs.BaseUrl) };
        http.DefaultRequestHeaders.Add("xi-api-key", options.ElevenLabs.ApiKey);
        var tts = new ElevenLabsTtsService(http, Options.Create(options));

        using var pcm = new MemoryStream();
        var sw = Stopwatch.StartNew();
        var chunks = 0;
        await foreach (var chunk in tts.SynthesizeStreamAsync(
            "Привет! Я Юки, твой голосовой ассистент. Сейчас проверим, как звучит потоковый синтез.",
            CancellationToken.None))
        {
            if (chunks++ == 0) _output.WriteLine($"Первый кусок: {sw.ElapsedMilliseconds} ms");
            Assert.Equal(0, chunk.Length % AudioFormat.Pcm16Mono24k.BlockAlign);
            pcm.Write(chunk.Span);
        }

        var seconds = (double)pcm.Length / AudioFormat.Pcm16Mono24k.BytesPerSecond;
        var path = Path.GetFullPath("out.wav");
        await File.WriteAllBytesAsync(path, Wav.Write(pcm.ToArray(), AudioFormat.Pcm16Mono24k));

        _output.WriteLine($"Кусков: {chunks}, всего {sw.ElapsedMilliseconds} ms, звук {seconds:F1} с → {path}");
        Assert.True(chunks > 1, "Ожидали несколько кусков — значит, звук идёт потоком.");
        Assert.True(seconds > 1);
    }
}
