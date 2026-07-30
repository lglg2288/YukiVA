using System.Collections.ObjectModel;
using YukiVA.Client.Wpf.Models;
using YukiVA.Client.Wpf.Mvvm;
using YukiVA.Client.Wpf.Services;

namespace YukiVA.Client.Wpf.ViewModels;

/// <summary>
/// Логика главного экрана. Ничего не знает про кнопки и окна — только данные и команды,
/// к которым "привязывается" разметка (MainWindow.xaml).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly VoiceApiClient _api;
    private readonly AudioRecorder _recorder;
    private readonly AudioPlayer _player;
    private readonly IToolProvider _tools;

    private Guid? _sessionId;          // null до первого ответа сервера
    private bool _toolsReady;          // удалось ли поднять источник инструментов

    /// <summary>История переписки. ObservableCollection — список, за изменениями которого WPF следит сам.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    /// <summary>Команда кнопки: первое нажатие — начать запись, второе — остановить и отправить.</summary>
    public AsyncRelayCommand ToggleRecordCommand { get; }

    public MainViewModel(VoiceApiClient api, AudioRecorder recorder, AudioPlayer player, IToolProvider tools)
    {
        _api = api;
        _recorder = recorder;
        _player = player;
        _tools = tools;

        ToggleRecordCommand = new AsyncRelayCommand(ToggleRecordAsync);
    }

    // ----- Свойства, на которые смотрит интерфейс -----

    private string _status = "Готов к записи";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
                OnPropertyChanged(nameof(RecordButtonText)); // текст кнопки зависит от этого
        }
    }

    /// <summary>Идёт сетевой запрос (для индикатора загрузки).</summary>
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string RecordButtonText => IsRecording ? "Стоп" : "Записать";

    // ----- Основной сценарий -----

    private async Task ToggleRecordAsync()
    {
        // Первое нажатие: просто запускаем запись и выходим.
        if (!IsRecording)
        {
            _recorder.Start();
            IsRecording = true;
            Status = "Идёт запись… нажми ещё раз, чтобы остановить";
            return;
        }

        // Второе нажатие: останавливаем запись и отправляем на сервер.
        IsRecording = false;
        IsBusy = true;
        try
        {
            Status = "Останавливаю запись…";
            byte[] wav = await _recorder.StopAsync();

            await EnsureToolsAsync();   // поднимаем инструменты (один раз)

            Status = "Отправляю на сервер…";
            var result = await _api.SendVoiceAsync(wav, _sessionId, _tools.ToolsJson);

            await ProcessTurnAsync(result);
        }
        catch (Exception ex)
        {
            Status = "Ошибка: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Обрабатываем ответ сервера. Если это вызов инструмента — выполняем его и
    /// отправляем результат обратно, повторяя цикл, пока не придёт обычный голосовой ответ.
    /// </summary>
    private async Task ProcessTurnAsync(VoiceTurnResult result)
    {
        for (int guard = 0; guard < 6; guard++)   // защита от бесконечного цикла
        {
            if (result.SessionId is Guid sid) _sessionId = sid;

            // Обычный голосовой ответ — показываем историю и проигрываем звук.
            if (!result.IsToolCall)
            {
                await RefreshHistoryAsync();
                if (result.AudioReply is { Length: > 0 })
                    _player.Play(result.AudioReply);
                Status = "Готов к записи";
                return;
            }

            // Иначе LLM просит выполнить инструмент.
            Status = $"LLM вызвал инструмент: {result.ToolName}";
            string toolText = await _tools.CallToolAsync(
                result.ToolName!, result.ToolArgumentsJson ?? "{}");

            // Отдаём результат серверу и получаем следующий ответ (аудио или ещё один вызов).
            result = await _api.SendToolResultAsync(_sessionId, toolText, _tools.ToolsJson);
        }

        Status = "Слишком много вызовов инструментов подряд — прервал.";
    }

    /// <summary>Перечитать всю переписку из сессии и обновить список на экране.</summary>
    private async Task RefreshHistoryAsync()
    {
        if (_sessionId is not Guid sid) return;
        try
        {
            var messages = await _api.GetMessagesAsync(sid);
            Messages.Clear();
            foreach (var m in messages) Messages.Add(m);
        }
        catch
        {
            // Для демо не критично — переписка просто не обновится.
        }
    }

    /// <summary>Поднять источник инструментов один раз. Если не вышло — работаем без инструментов.</summary>
    private async Task EnsureToolsAsync()
    {
        if (_toolsReady) return;
        try
        {
            await _tools.InitializeAsync();
            _toolsReady = true;
        }
        catch (Exception ex)
        {
            Status = "Инструменты недоступны, продолжаю без них: " + ex.Message;
        }
    }
}
