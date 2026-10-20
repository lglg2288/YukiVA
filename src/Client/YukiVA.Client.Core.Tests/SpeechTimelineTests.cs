using YukiVA.Client.Core.Conversation;

namespace YukiVA.Client.Core.Tests;

public class SpeechTimelineTests
{
    private readonly SpeechTimeline _timeline = new();

    public SpeechTimelineTests()
    {
        // Пузырь 1: два предложения; пузырь 2 (после tool calls) — одно.
        _timeline.Add(new SentenceQueued(default, Response: 1, CharStart: 0, CharLength: 10, AudioStartByte: 0));
        _timeline.Add(new SentenceQueued(default, Response: 1, CharStart: 11, CharLength: 8, AudioStartByte: 1000));
        _timeline.Add(new SentenceQueued(default, Response: 2, CharStart: 0, CharLength: 20, AudioStartByte: 1600));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(999, 1, 0)]
    [InlineData(1000, 1, 11)]
    [InlineData(1599, 1, 11)]
    [InlineData(1600, 2, 0)]
    [InlineData(2399, 2, 0)]
    public void Current_FindsSentenceByPlaybackPosition(long played, int response, int charStart)
    {
        var current = _timeline.Current(played, writtenBytes: 2400);

        Assert.Equal(new SpokenSentence(response, charStart, current!.Value.CharLength), current);
    }

    [Fact]
    public void Current_AfterAllAudioPlayed_IsNull() =>
        Assert.Null(_timeline.Current(playedBytes: 2400, writtenBytes: 2400));

    [Fact]
    public void Current_Empty_IsNull() =>
        Assert.Null(new SpeechTimeline().Current(0, 0));

    [Fact]
    public void Current_LastSentenceBeforeItsAudioArrived_IsNull() =>
        Assert.Null(_timeline.Current(playedBytes: 1600, writtenBytes: 1600));
}
