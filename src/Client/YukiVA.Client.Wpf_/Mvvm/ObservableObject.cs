using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YukiVA.Client.Wpf.Mvvm;

/// <summary>
/// Базовый класс для всех ViewModel.
/// Умеет уведомлять интерфейс (WPF) о том, что какое-то свойство поменялось,
/// чтобы привязки (Binding) сами обновили картинку на экране.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Сообщить интерфейсу: "свойство <paramref name="name"/> изменилось".</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Удобный помощник для сеттеров: записывает новое значение в поле и,
    /// если оно действительно изменилось, шлёт уведомление в интерфейс.
    /// Возвращает true, если значение поменялось.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
