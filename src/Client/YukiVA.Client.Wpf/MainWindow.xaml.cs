using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YukiVA.Client.Wpf.ViewModels;

namespace YukiVA.Client.Wpf;

/// <summary>Окно: только клавиатура, мышь и автопрокрутка — логика в MainViewModel.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        // Лог Latency: последние события всегда видны.
        ((INotifyCollectionChanged)LatencyLog.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LatencyLog.Items.Count > 0)
                LatencyLog.ScrollIntoView(LatencyLog.Items[^1]);
        };
    }

    // Своя шапка: кнопки окна и вид развёрнутого окна (без скругления и не под панель задач).
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        var maximized = WindowState == WindowState.Maximized;
        // Окно без системной рамки разворачивается на весь экран, закрывая панель задач.
        MaxWidth = maximized ? SystemParameters.WorkArea.Width : double.PositiveInfinity;
        MaxHeight = maximized ? SystemParameters.WorkArea.Height : double.PositiveInfinity;

        var radius = maximized ? 0 : 12;
        Frame.CornerRadius = new CornerRadius(radius);
        Frame.BorderThickness = new Thickness(maximized ? 0 : 1);
        Header.CornerRadius = new CornerRadius(radius, radius, 0, 0);
        Footer.CornerRadius = new CornerRadius(0, 0, radius, radius);

        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    // Push-to-talk: зажал пробел — говоришь, отпустил — ход уходит на сервер.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (!e.IsRepeat) _vm.StartListening();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.Interrupt();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override async void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key != Key.Space) return;

        e.Handled = true;
        await _vm.StopListeningAndSendAsync();
    }

    private void OnMicDown(object sender, MouseButtonEventArgs e)
    {
        MicButton.CaptureMouse();
        _vm.StartListening();
    }

    private async void OnMicUp(object sender, MouseButtonEventArgs e)
    {
        MicButton.ReleaseMouseCapture();
        await _vm.StopListeningAndSendAsync();
    }

    // Лента растёт (новые сообщения, печатающийся текст) — держим низ в поле зрения.
    private void OnChatScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0)
            ChatScroll.ScrollToEnd();
    }
}
