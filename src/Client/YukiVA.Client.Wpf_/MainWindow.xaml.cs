using System.Windows;

namespace YukiVA.Client.Wpf;

/// <summary>
/// Code-behind теперь пустой: вся логика живёт в MainViewModel.
/// DataContext (ViewModel) задаётся при создании окна в App.xaml.cs.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
