using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Wpf.Tools;

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Как показывать источник в интерфейсе.</summary>
    public string Name { get; set; } = "erp";
    public string Command { get; set; } = "docker";
    public string[] Arguments { get; set; } = Array.Empty<string>();
}

public enum McpStatus { Connecting, Ready, Unavailable }

/// <summary>
/// Инструменты из MCP-сервера (postgres-mcp над демо-ERP). Сервер запускается при старте приложения;
/// если он недоступен, ассистент работает без инструментов, а попытка повторяется не чаще раза в 30 с.
/// </summary>
public sealed class McpToolExecutor : IToolExecutor, IAsyncDisposable
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly McpOptions _options;
    private readonly ILogger<McpToolExecutor> _logger;
    private readonly SemaphoreSlim _connect = new(1, 1);

    private McpClient? _client;
    private IReadOnlyList<ToolDefinition> _tools = Array.Empty<ToolDefinition>();
    private DateTime _lastAttempt = DateTime.MinValue;

    public McpToolExecutor(IOptions<McpOptions> options, ILogger<McpToolExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Name => _options.Name;
    public McpStatus Status { get; private set; } = McpStatus.Connecting;
    public event Action<McpStatus>? StatusChanged;

    public async Task<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        return _tools;
    }

    public async Task<string> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        var client = _client ?? throw new InvalidOperationException($"MCP-сервер {Name} недоступен.");

        var result = await client.CallToolAsync(call.Name, ParseArguments(call.ArgumentsJson), cancellationToken: ct);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

        return result.IsError == true ? throw new InvalidOperationException(text) : text;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (Status == McpStatus.Ready || DateTime.UtcNow - _lastAttempt < RetryAfter)
            return;

        await _connect.WaitAsync(ct);
        try
        {
            if (Status == McpStatus.Ready || DateTime.UtcNow - _lastAttempt < RetryAfter)
                return;
            _lastAttempt = DateTime.UtcNow;
            SetStatus(McpStatus.Connecting);

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = _options.Name,
                Command = _options.Command,
                Arguments = _options.Arguments
            });
            _client = await McpClient.CreateAsync(transport, cancellationToken: ct);

            var tools = await _client.ListToolsAsync(cancellationToken: ct);
            _tools = tools.Select(t => new ToolDefinition(t.Name, t.Description ?? "", t.JsonSchema.GetRawText())).ToList();

            _logger.LogInformation("MCP {Name}: {Count} инструментов", _options.Name, _tools.Count);
            SetStatus(McpStatus.Ready);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MCP {Name} недоступен — работаем без инструментов", _options.Name);
            await DisposeClientAsync();
            SetStatus(McpStatus.Unavailable);
        }
        finally
        {
            _connect.Release();
        }
    }

    public async ValueTask DisposeAsync() => await DisposeClientAsync();

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _client = null;
        _tools = Array.Empty<ToolDefinition>();
    }

    private void SetStatus(McpStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    /// <summary>Аргументы приходят JSON-объектом (иногда — строкой с JSON внутри).</summary>
    private static Dictionary<string, object?> ParseArguments(string json)
    {
        using var outer = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var text = outer.RootElement.ValueKind == JsonValueKind.String
            ? outer.RootElement.GetString() ?? "{}"
            : outer.RootElement.GetRawText();

        using var inner = JsonDocument.Parse(text);
        var args = new Dictionary<string, object?>();
        if (inner.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in inner.RootElement.EnumerateObject())
                args[p.Name] = p.Value.Clone();
        }
        return args;
    }
}
