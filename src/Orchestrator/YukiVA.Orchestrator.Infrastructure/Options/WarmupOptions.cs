namespace YukiVA.Orchestrator.Infrastructure.Options;

/// <summary>Прогрев HTTPS-соединений к облачным API. Биндится из секции "Warmup".</summary>
public class WarmupOptions
{
    public const string SectionName = "Warmup";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Как часто «пинговать» API, чтобы соединение не закрылось по простою. 0 — только при старте.
    /// </summary>
    public int KeepAliveSeconds { get; set; } = 45;
}
