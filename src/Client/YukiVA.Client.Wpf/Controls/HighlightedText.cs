using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace YukiVA.Client.Wpf.Controls;

/// <summary>
/// Текст с подсвеченным фрагментом для TextBlock: Inlines не биндятся,
/// поэтому собираем их сами — до, подсветка, после.
/// </summary>
public static class HighlightedText
{
    public static readonly DependencyProperty TextProperty = Register("Text", typeof(string), "");
    public static readonly DependencyProperty StartProperty = Register("Start", typeof(int), 0);
    public static readonly DependencyProperty LengthProperty = Register("Length", typeof(int), 0);
    public static readonly DependencyProperty BrushProperty = Register("Brush", typeof(Brush), Brushes.Khaki);

    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string value) => o.SetValue(TextProperty, value);
    public static int GetStart(DependencyObject o) => (int)o.GetValue(StartProperty);
    public static void SetStart(DependencyObject o, int value) => o.SetValue(StartProperty, value);
    public static int GetLength(DependencyObject o) => (int)o.GetValue(LengthProperty);
    public static void SetLength(DependencyObject o, int value) => o.SetValue(LengthProperty, value);
    public static Brush GetBrush(DependencyObject o) => (Brush)o.GetValue(BrushProperty);
    public static void SetBrush(DependencyObject o, Brush value) => o.SetValue(BrushProperty, value);

    private static DependencyProperty Register(string name, Type type, object defaultValue) =>
        DependencyProperty.RegisterAttached(name, type, typeof(HighlightedText),
            new PropertyMetadata(defaultValue, (o, _) => Rebuild(o)));

    private static void Rebuild(DependencyObject o)
    {
        if (o is not TextBlock block) return;

        var text = GetText(block) ?? "";
        var start = Math.Clamp(GetStart(block), 0, text.Length);
        var length = Math.Clamp(GetLength(block), 0, text.Length - start);

        block.Inlines.Clear();
        if (length == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        block.Inlines.Add(new Run(text[..start]));
        block.Inlines.Add(new Run(text.Substring(start, length)) { Background = GetBrush(block) });
        block.Inlines.Add(new Run(text[(start + length)..]));
    }
}

/// <summary>true, если два значения совпадают как строки — например, текущий этап и Tag чипа.</summary>
public sealed class StageMatchesConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
