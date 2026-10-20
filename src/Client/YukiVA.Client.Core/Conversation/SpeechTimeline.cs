namespace YukiVA.Client.Core.Conversation;

/// <summary>Предложение, которое звучит сейчас: какой пузырь и какие символы в нём подсветить.</summary>
public readonly record struct SpokenSentence(int Response, int CharStart, int CharLength);

/// <summary>
/// Сопоставляет позицию воспроизведения с текстом: предложение звучит с его AudioStartByte
/// до начала следующего (у последнего — до конца уже записанного звука).
/// </summary>
public sealed class SpeechTimeline
{
    private readonly List<SentenceQueued> _sentences = new();

    public void Clear() => _sentences.Clear();

    public void Add(SentenceQueued sentence) => _sentences.Add(sentence);

    public SpokenSentence? Current(long playedBytes, long writtenBytes)
    {
        for (var i = _sentences.Count - 1; i >= 0; i--)
        {
            var s = _sentences[i];
            if (s.AudioStartByte > playedBytes)
                continue;

            var end = i + 1 < _sentences.Count ? _sentences[i + 1].AudioStartByte : writtenBytes;
            return playedBytes < end ? new SpokenSentence(s.Response, s.CharStart, s.CharLength) : null;
        }

        return null;
    }
}
