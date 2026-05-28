using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Infrastructure.Services;

internal sealed class TtsHttpService(
    HttpClient httpClient,
    ILogger<TtsHttpService> logger) : ITtsService
{
    public async Task<AudioChunk> SynthesizeAsync(string text, SpeechOptions speechOptions, CancellationToken ct = default)
    {
        var request = new TtsRequestDto(text, speechOptions.VoiceId, speechOptions.Language,
            speechOptions.Speed, speechOptions.Pitch);

        logger.LogDebug("POST /synthesize ({Chars} chars, voice={Voice})", text.Length, speechOptions.VoiceId);

        var response = await httpClient.PostAsJsonAsync("/synthesize", request, ct);
        response.EnsureSuccessStatusCode();

        // TTS services typically return raw audio bytes with Content-Type header.
        var audioData = await response.Content.ReadAsByteArrayAsync(ct);
        var format = DetectFormat(response.Content.Headers.ContentType?.MediaType);

        return new AudioChunk(audioData, format, SampleRate: 22050, Channels: 1);
    }

    private static string DetectFormat(string? mediaType) => mediaType switch
    {
        "audio/wav" or "audio/wave" => "wav",
        "audio/mpeg" => "mp3",
        "audio/ogg" => "ogg",
        "audio/opus" => "opus",
        _ => "wav"
    };

    private sealed record TtsRequestDto(
        string Text,
        string VoiceId,
        string Language,
        float Speed,
        float Pitch);
}
