using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;   // TextContentBlock
using YukiVA.Client.Wpf.Models;

namespace YukiVA.Client.Wpf.Services;

/// <summary>
/// Инструменты через MCP. Поднимает MCP-сервер (здесь — postgres в docker),
/// забирает у него список инструментов и умеет их вызывать.
/// </summary>
public sealed class McpToolProvider : IToolProvider
{
    private McpClient? _mcp;

    public string ToolsJson { get; private set; } = "[]";

    public async Task InitializeAsync()
    {
        if (_mcp is not null) return; // уже подняли

        // host.docker.internal = хост-машина с точки зрения контейнера (Docker Desktop, Win/Mac).
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "postgres",
            Command = "docker",
            Arguments = new[]
            {
                "run", "-i", "--rm",
                "-e", "DATABASE_URI=postgresql://postgres:postgres@host.docker.internal:5432/demo",
                "crystaldba/postgres-mcp",
                "--access-mode=unrestricted"   // для read-only демо поставь restricted
            }
        });

        _mcp = await McpClient.CreateAsync(transport);

        // Переводим инструменты MCP в формат серверного ToolDefinition.
        var tools = await _mcp.ListToolsAsync();
        var defs = tools.Select(t => new ToolDefinition(
            t.Name,
            t.Description ?? "",
            t.JsonSchema.GetRawText()    // JSON Schema параметров инструмента
        )).ToList();

        ToolsJson = JsonSerializer.Serialize(defs); // -> Name/Description/ParametersJsonSchema
    }

    public async Task<string> CallToolAsync(string name, string argumentsJson)
    {
        if (_mcp is null)
            throw new InvalidOperationException("MCP не инициализирован.");

        var args = ParseArgs(argumentsJson);
        var result = await _mcp.CallToolAsync(name, args);

        // Склеиваем все текстовые блоки ответа инструмента в одну строку.
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    public async ValueTask DisposeAsync()
    {
        if (_mcp is not null) await _mcp.DisposeAsync();
    }

    /// <summary>
    /// "arguments" с сервера может прийти как JSON-строка ("{\"x\":1}") или как объект —
    /// разбираем оба варианта в словарь имя->значение.
    /// </summary>
    private static Dictionary<string, object?> ParseArgs(string rawArguments)
    {
        using var outer = JsonDocument.Parse(rawArguments);

        // Если это строка — внутри неё ещё один JSON, разворачиваем его.
        var innerText = outer.RootElement.ValueKind == JsonValueKind.String
            ? (outer.RootElement.GetString() ?? "{}")
            : outer.RootElement.GetRawText();

        using var inner = JsonDocument.Parse(innerText);

        var dict = new Dictionary<string, object?>();
        if (inner.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var p in inner.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.Clone(); // Clone — чтобы значение пережило Dispose документа

        return dict;
    }
}
