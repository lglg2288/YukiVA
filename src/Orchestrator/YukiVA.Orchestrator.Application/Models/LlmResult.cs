using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Application.Models;

/// <summary>Результат обращения к LLM: либо финальный текст, либо запрос вызова инструмента.</summary>
public class LlmResult
{
    public bool IsToolCall { get; }

    public string? Text { get; }

    public string? ToolCallId { get; }
    public string? ToolName { get; }
    public string? ToolArgumentsJson { get; }

    private LlmResult(bool isToolCall, string? text,
        string? toolCallId, string? toolName, string? toolArgumentsJson)
    {
        IsToolCall = isToolCall;
        Text = text;
        ToolCallId = toolCallId;
        ToolName = toolName;
        ToolArgumentsJson = toolArgumentsJson;
    }

    public static LlmResult FromText(string text) =>
        new(false, text, null, null, null);

    public static LlmResult FromToolCall(string toolCallId, string toolName, string argumentsJson) =>
        new(true, null, toolCallId, toolName, argumentsJson);
}
