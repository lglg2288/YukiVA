namespace YukiVA.Orchestrator.Infrastructure.Options;

public sealed class LlmOptions
{
    public const string Section = "Services:Llm";
    public string BaseUrl { get; init; } = "http://localhost:5002";
    public string Model { get; init; } = "default";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
