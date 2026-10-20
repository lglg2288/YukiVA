using System.Windows.Input;

namespace YukiVA.Client.Wpf.Mvvm;

/// <summary>
/// Команда для кнопок (привязывается через Command="{Binding ...}").
/// Поддерживает асинхронную работу: пока выполняется задача, кнопка
/// автоматически становится недоступной (чтобы не нажали дважды).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    // Кнопка доступна, если задача сейчас не выполняется и (опционально) разрешено условие.
    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _isRunning = true;
        RaiseCanExecuteChanged();      // -> кнопка погасла
        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();  // -> кнопка снова активна
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
