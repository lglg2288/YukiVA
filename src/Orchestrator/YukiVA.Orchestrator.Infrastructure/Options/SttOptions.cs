namespace YukiVA.Orchestrator.Infrastructure.Options;

public sealed class SttOptions
{
    public const string Section = "Services:Stt";
    public string BaseUrl { get; init; } = "http://localhost:5001";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
