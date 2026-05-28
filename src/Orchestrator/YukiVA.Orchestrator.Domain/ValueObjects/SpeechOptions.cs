namespace YukiVA.Orchestrator.Domain.ValueObjects;

public sealed record SpeechOptions(
    string VoiceId = "default",
    string Language = "ru-RU",
    float Speed = 1.0f,
    float Pitch = 0.0f);
