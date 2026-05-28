namespace YukiVA.Orchestrator.Domain.ValueObjects;

/// <param name="Data">Raw audio bytes.</param>
/// <param name="Format">Container/codec: "wav", "mp3", "opus".</param>
/// <param name="SampleRate">Samples per second, e.g. 16000 or 44100.</param>
/// <param name="Channels">1 = mono, 2 = stereo.</param>
public sealed record AudioChunk(
    byte[] Data,
    string Format,
    int SampleRate,
    int Channels = 1);
