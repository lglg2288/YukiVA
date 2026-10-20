namespace YukiVA.Orchestrator.Infrastructure.Options;

/// <summary>Выбор и настройки синтеза речи. Биндится из секции "Tts".</summary>
public class TtsOptions
{
    public const string SectionName = "Tts";

    public const string ElevenLabsProvider = "ElevenLabs";
    public const string XttsProvider = "Xtts";

    /// <summary>ElevenLabs | Xtts.</summary>
    public string Provider { get; set; } = ElevenLabsProvider;

    public ElevenLabsOptions ElevenLabs { get; set; } = new();
}

public class ElevenLabsOptions
{
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io";
    public string ApiKey { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public string ModelId { get; set; } = "eleven_flash_v2_5";

    /// <summary>ISO 639-1. Пусто — не отправлять (multilingual_v2 его не поддерживает).</summary>
    public string? LanguageCode { get; set; } = "ru";
}
