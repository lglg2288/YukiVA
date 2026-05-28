using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using YukiVA.Orchestrator.Domain.Interfaces;
using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Infrastructure.Services;

internal sealed class SttHttpService(
    HttpClient httpClient,
    ILogger<SttHttpService> logger) : ISttService
{
    public async Task<TranscriptResult> TranscribeAsync(AudioChunk audio, CancellationToken ct = default)
    {
        logger.LogDebug("POST /transcribe ({Bytes} bytes, {Format})", audio.Data.Length, audio.Format);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(audio.Data), "audio", $"audio.{audio.Format}");
        form.Add(new StringContent(audio.Format), "format");
        form.Add(new StringContent(audio.SampleRate.ToString()), "sample_rate");
        form.Add(new StringContent(audio.Channels.ToString()), "channels");

        var response = await httpClient.PostAsync("/transcribe", form, ct);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SttResponseDto>(ct)
            ?? throw new InvalidOperationException("STT service returned an empty response.");

        return new TranscriptResult(dto.Text, dto.Confidence, dto.Language,
            TimeSpan.FromSeconds(dto.DurationSeconds));
    }

    // DTO is private — only the HTTP adapter needs to know the wire format.
    private sealed record SttResponseDto(
        string Text,
        double Confidence,
        string Language,
        double DurationSeconds);
}
