using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

/// <summary>Результат голосового хода: аудио-ответ + распознанный/сгенерированный текст.</summary>
public record ProcessVoiceTurnResult(
    Guid SessionPublicId,
    string UserText,
    string AssistantText,
    byte[] AudioReply);
