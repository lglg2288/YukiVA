using YukiVA.Orchestrator.Application.Abstractions;
using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

public class ProcessVoiceTurnHandler
{
    private readonly ISpeechToText _stt;
    private readonly ILlmService _llm;
    private readonly ITextToSpeech _tts;
    private readonly ISessionRepository _sessions;

    public ProcessVoiceTurnHandler(
        ISpeechToText stt,
        ILlmService llm,
        ITextToSpeech tts,
        ISessionRepository sessions)
    {
        _stt = stt;
        _llm = llm;
        _tts = tts;
        _sessions = sessions;
    }

    public async Task<ProcessVoiceTurnResult> HandleAsync(
        byte[] audio,
        Guid? sessionPublicId,
        CancellationToken cancellationToken = default)
    {
        ConversationSession session;
        if (sessionPublicId is Guid id)
        {
            session = await _sessions.GetByPublicIdAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"Сессия {id} не найдена.");
        }
        else
        {
            session = new ConversationSession();
            await _sessions.AddAsync(session, cancellationToken);
        }

        var userText = await _stt.TranscribeAsync(audio, cancellationToken);
        session.AddMessage(MessageRole.User, userText);

        var assistantText = await _llm.CompleteAsync(session.Messages, cancellationToken);
        session.AddMessage(MessageRole.Assistant, assistantText);

        await _sessions.SaveChangesAsync(cancellationToken);

        var audioReply = await _tts.SynthesizeAsync(assistantText, cancellationToken);

        return new ProcessVoiceTurnResult(
            session.PublicId, userText, assistantText, audioReply);
    }
}
