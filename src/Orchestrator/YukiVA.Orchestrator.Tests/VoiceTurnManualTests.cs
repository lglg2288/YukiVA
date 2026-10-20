using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Infrastructure.Options;
using YukiVA.Orchestrator.Infrastructure.Services;

namespace YukiVA.Orchestrator.Tests;

/// <summary>
/// Сквозной ход с настоящими DeepSeek и ElevenLabs (STT подставной): сколько ждать первого текста и звука.
/// Пишет turn.wav. Нужны ключи в user-secrets Api; VoiceId — Tts__ElevenLabs__VoiceId или из user-secrets.
/// </summary>
[Trait("Category", "Manual")]
public class VoiceTurnManualTests
{
    private const string ApiUserSecretsId = "f2ca71cb-aea5-43f0-b6a0-21320c95bac4";
    private const string DefaultVoiceId = "EXAVITQu4vr4xnSDxMaL";

    private readonly ITestOutputHelper _output;

    public VoiceTurnManualTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RealServices_TimeToFirstTextAndAudio()
    {
        var config = new ConfigurationBuilder().AddUserSecrets(ApiUserSecretsId).AddEnvironmentVariables().Build();
        var llmOptions = config.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ttsOptions = config.GetSection(TtsOptions.SectionName).Get<TtsOptions>() ?? new TtsOptions();
        if (string.IsNullOrWhiteSpace(llmOptions.ApiKey) || string.IsNullOrWhiteSpace(ttsOptions.ElevenLabs.ApiKey))
        {
            _output.WriteLine("Ключи Llm/Tts не заданы — пропускаем.");
            return;
        }
        if (string.IsNullOrWhiteSpace(ttsOptions.ElevenLabs.VoiceId))
            ttsOptions.ElevenLabs.VoiceId = DefaultVoiceId;

        var llmHttp = new HttpClient { BaseAddress = new Uri(llmOptions.BaseUrl) };
        llmHttp.DefaultRequestHeaders.Authorization = new("Bearer", llmOptions.ApiKey);
        var ttsHttp = new HttpClient { BaseAddress = new Uri(ttsOptions.ElevenLabs.BaseUrl) };
        ttsHttp.DefaultRequestHeaders.Add("xi-api-key", ttsOptions.ElevenLabs.ApiKey);

        var handler = new ProcessVoiceTurnHandler(
            new FixedStt("Расскажи коротко, в трёх предложениях, чем полезен потоковый синтез речи."),
            new DeepSeekLlmService(llmHttp, Options.Create(llmOptions)),
            new ElevenLabsTtsService(ttsHttp, Options.Create(ttsOptions)),
            new InMemorySessions());

        using var pcm = new MemoryStream();
        var sw = Stopwatch.StartNew();
        long? firstText = null, firstAudio = null;

        await foreach (var e in handler.HandleVoiceAsync(Array.Empty<byte>(), null, Array.Empty<ToolDefinition>()))
        {
            switch (e)
            {
                case AssistantTextDelta d:
                    firstText ??= sw.ElapsedMilliseconds;
                    break;
                case AudioChunk a:
                    if (firstAudio is null) _output.WriteLine($"{sw.ElapsedMilliseconds,6} ms  первый звук");
                    firstAudio ??= sw.ElapsedMilliseconds;
                    pcm.Write(a.Pcm.Span);
                    break;
                default:
                    _output.WriteLine($"{sw.ElapsedMilliseconds,6} ms  {e.GetType().Name}");
                    break;
            }
        }

        var path = Path.GetFullPath("turn.wav");
        await File.WriteAllBytesAsync(path, Wav.Write(pcm.ToArray(), AudioFormat.Pcm16Mono24k));
        _output.WriteLine($"Первый текст: {firstText} ms, первый звук: {firstAudio} ms, всего: {sw.ElapsedMilliseconds} ms → {path}");

        Assert.NotNull(firstAudio);
    }

    private sealed class FixedStt(string text) : ISpeechToText
    {
        public Task<string> TranscribeAsync(byte[] audio, CancellationToken cancellationToken = default) =>
            Task.FromResult(text);
    }

    private sealed class InMemorySessions : ISessionRepository
    {
        private readonly List<ConversationSession> _sessions = new();

        public Task AddAsync(ConversationSession session, CancellationToken cancellationToken = default)
        {
            _sessions.Add(session);
            return Task.CompletedTask;
        }

        public Task<ConversationSession?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.FirstOrDefault(s => s.PublicId == publicId));

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
