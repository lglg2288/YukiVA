using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YukiVA.Client.Core.Tools;

namespace YukiVA.Client.Wpf.ViewModels;

public sealed class UserMessageViewModel(string text)
{
    public string Text { get; } = text;
}

public sealed class ErrorMessageViewModel(string text)
{
    public string Text { get; } = text;
}

/// <summary>Пузырь ассистента: текст растёт по дельтам, звучащее предложение подсвечено.</summary>
public sealed partial class AssistantMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private int _highlightStart;
    [ObservableProperty] private int _highlightLength;

    public void Append(string delta) => Text += delta;

    public void Highlight(int start, int length)
    {
        HighlightStart = start;
        HighlightLength = length;
    }

    public void ClearHighlight() => HighlightLength = 0;
}

public enum ToolCallState { Running, Done, Failed }

/// <summary>Карточка вызова инструмента: что вызвано, сколько заняло, что вернулось.</summary>
public sealed partial class ToolCallViewModel : ObservableObject
{
    private const int PreviewLength = 140;

    public ToolCallViewModel(ToolCall call)
    {
        Id = call.Id;
        Name = call.Name;
        Arguments = Describe(call.ArgumentsJson);
    }

    public string Id { get; }
    public string Name { get; }

    /// <summary>Для SQL-инструментов — сам запрос, иначе — аргументы как есть.</summary>
    public string Arguments { get; }

    [ObservableProperty] private ToolCallState _state = ToolCallState.Running;
    [ObservableProperty] private string _duration = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private string _resultPreview = "";
    [ObservableProperty] private bool _isExpanded;

    public void Complete(string result, TimeSpan duration, bool failed)
    {
        State = failed ? ToolCallState.Failed : ToolCallState.Done;
        Duration = $"{duration.TotalMilliseconds:0} мс";
        Result = result;
        var oneLine = result.ReplaceLineEndings(" ");
        ResultPreview = oneLine.Length > PreviewLength ? oneLine[..PreviewLength] + "…" : oneLine;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private static string Describe(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("sql", out var sql)
                && sql.ValueKind == JsonValueKind.String)
                return sql.GetString()!.Trim();
        }
        catch (JsonException)
        {
        }
        return argumentsJson;
    }
}

/// <summary>Вызовы одного ответа модели — стоят рядом в одной строке ленты.</summary>
public sealed class ToolCallGroupViewModel(int response)
{
    public int Response { get; } = response;
    public ObservableCollection<ToolCallViewModel> Calls { get; } = new();
}
