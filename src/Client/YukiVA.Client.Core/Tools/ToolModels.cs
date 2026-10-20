namespace YukiVA.Client.Core.Tools;

/// <summary>Инструмент, который клиент предлагает модели (уходит на сервер в поле tools).</summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <summary>Вызов инструмента, который запросила модель.</summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>Результат исполнения вызова — уходит на сервер в /api/voice/tool-results.</summary>
public sealed record ToolResult(string ToolCallId, string Content);

/// <summary>Источник и исполнитель инструментов (в приложении — MCP-сервер).</summary>
public interface IToolExecutor
{
    /// <summary>Доступные инструменты. Источник недоступен — пустой список, ассистент работает без них.</summary>
    Task<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct);

    /// <summary>Исполнить вызов и вернуть результат текстом. Ошибка инструмента — исключение.</summary>
    Task<string> ExecuteAsync(ToolCall call, CancellationToken ct);
}
