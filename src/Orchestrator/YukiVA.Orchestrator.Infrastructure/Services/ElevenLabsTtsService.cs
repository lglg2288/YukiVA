using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Application.Audio;
using YukiVA.Orchestrator.Infrastructure.Options;

namespace YukiVA.Orchestrator.Infrastructure.Services;

/// <summary>Облачный TTS ElevenLabs: HTTP-стрим сырого PCM 24 кГц.</summary>
public class ElevenLabsTtsService : ITextToSpeech
{
    // pcm_24000 — ровно AudioFormat.Pcm16Mono24k, конвертировать ничего не нужно.
    private const string OutputFormat = "pcm_24000";
    private const int ReadBufferSize = 8 * 1024;

    private readonly HttpClient _http;
    private readonly ElevenLabsOptions _options;

    public ElevenLabsTtsService(HttpClient http, IOptions<TtsOptions> options)
    {
        _http = http;
        _options = options.Value.ElevenLabs;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamAsync(
        string text,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.VoiceId))
            throw new InvalidOperationException("Не задан Tts:ElevenLabs:VoiceId.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/text-to-speech/{Uri.EscapeDataString(_options.VoiceId)}/stream?output_format={OutputFormat}")
        {
            Content = JsonContent.Create(new SpeechRequest
            {
                Text = text,
                ModelId = _options.ModelId,
                LanguageCode = string.IsNullOrWhiteSpace(_options.LanguageCode) ? null : _options.LanguageCode
            })
        };

        // ResponseHeadersRead — отдавать звук по мере синтеза, а не после всего текста.
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ElevenLabs вернул {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(ct);

        // Сеть может разрезать сэмпл пополам — хвост переносим в следующий кусок.
        var blockAlign = AudioFormat.Pcm16Mono24k.BlockAlign;
        var buffer = new byte[ReadBufferSize];
        var pending = 0;
        int read;
        while ((read = await body.ReadAsync(buffer.AsMemory(pending), ct)) > 0)
        {
            var available = pending + read;
            var whole = available - available % blockAlign;
            if (whole > 0)
                yield return buffer.AsSpan(0, whole).ToArray();

            pending = available - whole;
            buffer.AsSpan(whole, pending).CopyTo(buffer);
        }
    }

    private class SpeechRequest
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("model_id")] public string ModelId { get; set; } = "";
        [JsonPropertyName("language_code")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LanguageCode { get; set; }
    }
}
