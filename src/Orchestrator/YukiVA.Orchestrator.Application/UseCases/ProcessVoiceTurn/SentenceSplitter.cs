using System.Text;

namespace YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

/// <summary>
/// Режет поток дельт LLM на куски для TTS: по концу предложения (. ! ? … и перевод строки),
/// если за знаком пробел. Не режет числа (3.5), сокращения (т.е., и т.д.) и инициалы.
/// Слишком короткие куски склеивает со следующими; первый кусок может отдать по запятой,
/// чтобы звук начался раньше.
/// </summary>
public sealed class SentenceSplitter
{
    /// <summary>Короче — интонация рвётся, лучше склеить со следующим предложением.</summary>
    public const int MinLength = 15;

    /// <summary>Первый кусок без конца предложения отдаётся по запятой не раньше этой длины.</summary>
    public const int FirstChunkCommaLength = 40;

    private const string Terminators = ".!?…";
    private const string Closers = "\"'»”)]";
    private const string Pauses = ",;:";

    // Сокращения, после которых точка — не конец предложения (без учёта регистра).
    // Однобуквенные слова (инициалы, «т.е.», «г.») отсекаются отдельно.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "др", "пр", "см", "напр", "им", "ул", "стр", "рис", "тыс", "млн", "млрд", "гг", "вв",
        "mr", "mrs", "ms", "dr", "vs"
    };

    private readonly StringBuilder _buffer = new();
    private bool _emittedAny;

    /// <summary>Добавить дельту, вернуть готовые куски (возможно, ни одного).</summary>
    public IReadOnlyList<string> Append(string delta)
    {
        _buffer.Append(delta);

        var ready = new List<string>();
        while (TryCut() is { } piece)
            ready.Add(piece);
        return ready;
    }

    /// <summary>Конец потока: отдать остаток, даже короткий.</summary>
    public string? Flush()
    {
        var rest = _buffer.ToString().Trim();
        _buffer.Clear();
        return rest.Length == 0 ? null : rest;
    }

    private string? TryCut()
    {
        var text = _buffer.ToString();

        for (var i = 0; i < text.Length; i++)
        {
            var end = SentenceEnd(text, i);
            if (end < 0 || text[..(end + 1)].Trim().Length < MinLength)
                continue;
            return Cut(end + 1);
        }

        if (!_emittedAny)
        {
            for (var i = FirstChunkCommaLength - 1; i + 1 < text.Length; i++)
            {
                if (Pauses.Contains(text[i]) && char.IsWhiteSpace(text[i + 1]))
                    return Cut(i + 1);
            }
        }

        return null;
    }

    /// <summary>Индекс последнего символа предложения, которое кончается на позиции i, иначе -1.</summary>
    private static int SentenceEnd(string text, int i)
    {
        if (text[i] == '\n')
            return i;
        if (!Terminators.Contains(text[i]))
            return -1;

        // «?!», «...», «!»» — конец предложения после всей серии знаков и закрывающих кавычек.
        var end = i;
        while (end + 1 < text.Length && (Terminators.Contains(text[end + 1]) || Closers.Contains(text[end + 1])))
            end++;

        // Что после знака, ещё не пришло — ждём следующую дельту (или Flush).
        if (end + 1 >= text.Length || !char.IsWhiteSpace(text[end + 1]))
            return -1;

        if (end == i && text[i] == '.' && IsAbbreviation(text, i))
            return -1;

        return end;
    }

    private static bool IsAbbreviation(string text, int dot)
    {
        var start = dot;
        while (start > 0 && char.IsLetter(text[start - 1]))
            start--;

        var word = text[start..dot];
        return word.Length == 1 || Abbreviations.Contains(word);
    }

    private string Cut(int length)
    {
        var piece = _buffer.ToString(0, length).Trim();
        _buffer.Remove(0, length);
        _emittedAny = true;
        return piece;
    }
}
