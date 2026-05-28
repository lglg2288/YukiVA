namespace YukiVA.Orchestrator.Domain.ValueObjects;

public sealed record TranscriptResult(
    string Text,
    double Confidence,
    string Language,
    TimeSpan Duration);
