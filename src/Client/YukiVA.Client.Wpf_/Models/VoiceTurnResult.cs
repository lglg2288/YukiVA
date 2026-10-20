namespace YukiVA.Client.Wpf.Models;

/// <summary>
/// Результат одного обращения к /api/voice или /api/voice/tool-result.
/// Сервер отвечает одним из двух способов, поэтому здесь два "набора" полей:
///   1) обычный голосовой ответ  -> IsToolCall = false, заполнены AssistantText + AudioReply;
///   2) запрос на вызов инструмента -> IsToolCall = true, заполнены ToolName + ToolArgumentsJson.
/// </summary>
public sealed class VoiceTurnResult
{
    /// <summary>Id сессии из заголовка X-Session-Id (нужно слать в следующих запросах).</summary>
    public Guid? SessionId { get; init; }

    public bool IsToolCall { get; init; }

    // --- если это голосовой ответ ---
    public string? AssistantText { get; init; }   // текст ответа (из заголовка X-Assistant-Text)
    public byte[]? AudioReply { get; init; }       // WAV для проигрывания

    // --- если это вызов инструмента ---
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArgumentsJson { get; init; } // аргументы (сырой JSON: строка или объект)
}
