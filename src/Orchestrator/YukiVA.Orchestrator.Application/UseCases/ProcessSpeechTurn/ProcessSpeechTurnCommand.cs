using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessSpeechTurn;

public sealed record ProcessSpeechTurnCommand(
    Guid SessionId,
    AudioChunk InputAudio);
