namespace YukiVA.Client.Wpf.Services;

/// <summary>
/// Источник инструментов для LLM. ViewModel работает только с этим интерфейсом
/// и не знает, что "под капотом" — MCP, заглушка или что-то ещё.
/// </summary>
public interface IToolProvider : IAsyncDisposable
{
    /// <summary>Список инструментов в виде JSON — ровно то, что уходит в поле формы "tools".</summary>
    string ToolsJson { get; }

    /// <summary>Подключиться к источнику и собрать список инструментов.</summary>
    Task InitializeAsync();

    /// <summary>Выполнить инструмент и вернуть его текстовый результат.</summary>
    Task<string> CallToolAsync(string name, string argumentsJson);
}
