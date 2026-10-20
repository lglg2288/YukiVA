using YukiVA.Orchestrator.Application.UseCases.ProcessVoiceTurn;

namespace YukiVA.Orchestrator.Tests;

public class SentenceSplitterTests
{
    public static TheoryData<string, string[]> Cases => new()
    {
        {
            "Привет, как твои дела? У меня всё хорошо, спасибо.",
            new[] { "Привет, как твои дела?", "У меня всё хорошо, спасибо." }
        },
        {
            // Дробное число не режется.
            "Число пи примерно равно 3.14 и это известно. Второе предложение тут.",
            new[] { "Число пи примерно равно 3.14 и это известно.", "Второе предложение тут." }
        },
        {
            // Сокращения т.е., т.д., и т.п. — не конец предложения.
            "Возьми хлеб, молоко и т.д. и т.п. потом иди домой. Это т.е. пример предложения.",
            new[] { "Возьми хлеб, молоко и т.д. и т.п. потом иди домой.", "Это т.е. пример предложения." }
        },
        {
            // Инициалы.
            "Поэт А. С. Пушкин родился в Москве. Это известный факт.",
            new[] { "Поэт А. С. Пушкин родился в Москве.", "Это известный факт." }
        },
        {
            // Слишком короткое предложение склеивается со следующим.
            "Да! Конечно, сейчас всё расскажу подробнее.",
            new[] { "Да! Конечно, сейчас всё расскажу подробнее." }
        },
        {
            // Первый кусок — по запятой после ~40 символов, чтобы звук начался раньше.
            "Сегодня мы поговорим о потоковой передаче данных, и это довольно интересная тема для разговора",
            new[] { "Сегодня мы поговорим о потоковой передаче данных,", "и это довольно интересная тема для разговора" }
        },
        {
            // По запятой режется только первый кусок.
            "Это первое предложение ответа, оно короткое. А во втором предложении, которое длиннее сорока символов, запятые есть, но резать по ним уже не нужно.",
            new[]
            {
                "Это первое предложение ответа, оно короткое.",
                "А во втором предложении, которое длиннее сорока символов, запятые есть, но резать по ним уже не нужно."
            }
        },
        {
            // Перевод строки — граница.
            "Первый пункт списка дел\nВторой пункт списка дел",
            new[] { "Первый пункт списка дел", "Второй пункт списка дел" }
        },
        {
            // Закрывающие кавычки остаются с предложением.
            "Он сказал: «Всё готово!» Потом ушёл домой спать.",
            new[] { "Он сказал: «Всё готово!»", "Потом ушёл домой спать." }
        },
        {
            // Многоточие и «?!».
            "Дай подумать немного… Ты правда это сделал?! Вот это да, молодец.",
            new[] { "Дай подумать немного…", "Ты правда это сделал?!", "Вот это да, молодец." }
        },
        {
            // Точка в конце без пробела — отдаётся при Flush.
            "Одно-единственное предложение.",
            new[] { "Одно-единственное предложение." }
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Split_WholeText(string text, string[] expected) =>
        Assert.Equal(expected, Split(text, text.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Split_ByOneChar_SameResult(string text, string[] expected) =>
        Assert.Equal(expected, Split(text, 1));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Split_ByThreeChars_SameResult(string text, string[] expected) =>
        Assert.Equal(expected, Split(text, 3));

    [Fact]
    public void Append_ReturnsSentenceAsSoonAsNextCharIsKnown()
    {
        var splitter = new SentenceSplitter();

        Assert.Empty(splitter.Append("Это законченное предложение."));
        Assert.Equal(new[] { "Это законченное предложение." }, splitter.Append(" Дальше"));
        Assert.Equal("Дальше", splitter.Flush());
    }

    [Fact]
    public void Flush_Empty_ReturnsNull()
    {
        var splitter = new SentenceSplitter();
        splitter.Append("   ");
        Assert.Null(splitter.Flush());
    }

    private static List<string> Split(string text, int deltaSize)
    {
        var splitter = new SentenceSplitter();
        var pieces = new List<string>();

        for (var i = 0; i < text.Length; i += deltaSize)
            pieces.AddRange(splitter.Append(text.Substring(i, Math.Min(deltaSize, text.Length - i))));

        if (splitter.Flush() is { } rest)
            pieces.Add(rest);
        return pieces;
    }
}
