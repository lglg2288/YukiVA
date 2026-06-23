using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

/// <summary>Итог хода: либо аудио-ответ, либо запрос вызова инструмента у клиента.</summary>
public class ProcessVoiceTurnResult
{
    public Guid SessionPublicId { get; }
    public bool IsToolCall { get; }

    public string? UserText { get; }
    public string? AssistantText { get; }
    public byte[]? AudioReply { get; }

    public string? ToolCallId { get; }
    public string? ToolName { get; }
    public string? ToolArgumentsJson { get; }

    private ProcessVoiceTurnResult(
        Guid sessionPublicId, bool isToolCall,
        string? userText, string? assistantText, byte[]? audioReply,
        string? toolCallId, string? toolName, string? toolArgumentsJson)
    {
        SessionPublicId = sessionPublicId;
        IsToolCall = isToolCall;
        UserText = userText;
        AssistantText = assistantText;
        AudioReply = audioReply;
        ToolCallId = toolCallId;
        ToolName = toolName;
        ToolArgumentsJson = toolArgumentsJson;
    }

    public static ProcessVoiceTurnResult Audio(
        Guid sessionId, string userText, string assistantText, byte[] audio) =>
        new(sessionId, false, userText, assistantText, audio, null, null, null);

    public static ProcessVoiceTurnResult ToolCall(
        Guid sessionId, string toolCallId, string toolName, string argumentsJson) =>
        new(sessionId, true, null, null, null, toolCallId, toolName, argumentsJson);
}
