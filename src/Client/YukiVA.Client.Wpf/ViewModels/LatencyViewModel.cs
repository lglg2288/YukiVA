using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YukiVA.Client.Core.Conversation;

namespace YukiVA.Client.Wpf.ViewModels;

/// <summary>Полоса водопада: отрезок [From, To] от конца речи.</summary>
public sealed partial class LatencyBarViewModel(string label) : ObservableObject
{
    public const double TrackWidth = 150;

    public string Label { get; } = label;

    [ObservableProperty] private double _left;
    [ObservableProperty] private double _width;
    [ObservableProperty] private string _value = "—";

    internal TimeSpan? From { get; set; }
    internal TimeSpan? To { get; set; }

    internal void Layout(TimeSpan scale)
    {
        if (From is not { } from || To is not { } to)
        {
            Left = Width = 0;
            Value = "—";
            return;
        }

        Left = TrackWidth * from / scale;
        Width = Math.Max(2, TrackWidth * (to - from) / scale);
        Value = $"{to.TotalMilliseconds:N0}";
    }
}

/// <summary>Панель Latency: водопад задержек текущего хода и лог SSE-событий.</summary>
public sealed partial class LatencyViewModel : ObservableObject
{
    private const int MaxLogLines = 200;

    private TimeSpan? _transcript, _firstText, _firstAudio, _total;
    private TimeSpan _latest;

    public LatencyViewModel()
    {
        Bars = new[] { Stt, FirstToken, FirstAudio, Answer };
    }

    public LatencyBarViewModel Stt { get; } = new("STT");
    public LatencyBarViewModel FirstToken { get; } = new("1-й токен");
    public LatencyBarViewModel FirstAudio { get; } = new("1-й звук");
    public LatencyBarViewModel Answer { get; } = new("Ответ");
    public IReadOnlyList<LatencyBarViewModel> Bars { get; }

    public ObservableCollection<string> Log { get; } = new();

    [ObservableProperty] private int _turn;
    [ObservableProperty] private string _tools = "";

    public void BeginTurn()
    {
        Turn++;
        _transcript = _firstText = _firstAudio = _total = null;
        _latest = TimeSpan.Zero;
        Tools = "";
        Log.Clear();
        Relayout();
    }

    public void Apply(TurnUpdate update)
    {
        _latest = update.Elapsed > _latest ? update.Elapsed : _latest;

        switch (update)
        {
            case ServerEventLogged e:
                if (e.Name == "transcript") _transcript ??= e.Elapsed;
                if (e.Name == "text") _firstText ??= e.Elapsed;
                if (e.Name == "audio") _firstAudio ??= e.Elapsed;
                AddLog($"+{e.Elapsed.TotalSeconds:0.00}  {e.Name}{(e.Detail is null ? "" : "  " + Shorten(e.Detail))}");
                break;

            case ToolCallFinished t:
                AddLog($"+{t.Elapsed.TotalSeconds:0.00}  tool {t.Call.Name} · {t.Duration.TotalMilliseconds:0} мс");
                break;

            case TurnFinished f:
                _total = f.Timings.Total;
                Tools = f.Timings.Tools > TimeSpan.Zero ? $"Инструменты: {f.Timings.Tools.TotalMilliseconds:N0} мс" : "";
                break;

            case TurnFailed failed:
                AddLog($"+{failed.Elapsed.TotalSeconds:0.00}  ошибка: {failed.Message}");
                break;
        }

        Relayout();
    }

    private void Relayout()
    {
        Stt.From = TimeSpan.Zero; Stt.To = _transcript;
        FirstToken.From = _transcript; FirstToken.To = _firstText;
        FirstAudio.From = _firstText; FirstAudio.To = _firstAudio;
        Answer.From = _transcript; Answer.To = _total;

        var scale = _total ?? _latest;
        if (scale <= TimeSpan.Zero) scale = TimeSpan.FromSeconds(1);
        foreach (var bar in Bars)
            bar.Layout(scale);
    }

    private void AddLog(string line)
    {
        Log.Add(line);
        if (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }

    private static string Shorten(string text) => text.Length > 28 ? text[..28] + "…" : text;
}
