namespace YukiVA.Orchestrator.Infrastructure.Options;

public sealed class TtsOptions
{
    public const string Section = "Services:Tts";
    public string BaseUrl { get; init; } = "http://localhost:5003";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
