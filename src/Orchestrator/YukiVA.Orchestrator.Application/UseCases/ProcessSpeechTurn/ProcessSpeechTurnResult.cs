using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessSpeechTurn;

public sealed record ProcessSpeechTurnResult(
    AudioChunk OutputAudio,
    string TranscribedText,
    string AssistantText);
