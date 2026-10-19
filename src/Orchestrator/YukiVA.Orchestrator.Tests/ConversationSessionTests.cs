using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Tests;

public class ConversationSessionTests
{
    private static readonly ToolCall CallA = new("call_a", "query", """{"sql":"select 1"}""");
    private static readonly ToolCall CallB = new("call_b", "list_tables", "{}");

    [Fact]
    public void AddUserMessage_AddsMessageToSession()
    {
        var session = new ConversationSession();        //Arrange
        session.AddUserMessage("hello");                //Act
        Assert.Single(session.Messages);                //Assert
    }
    [Fact]
    public void AddUserMessage_SetsRoleAndText()
    {
        var session = new ConversationSession();
        var message = session.AddUserMessage("how are you?");
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("how are you?", message.Text);
        Assert.Empty(message.ToolCalls);
        Assert.Null(message.ToolCallId);
    }
    [Fact]
    public void AddUserMessage_EmptyText_ThrowsArgumentException()
    {
        var session = new ConversationSession();

        // Если исключения НЕ будет — тест упадёт.
        Assert.Throws<ArgumentException>(
            () => session.AddUserMessage("   "));
    }
    [Fact]
    public void NewSession_HasUniquePublicId()
    {
        var a = new ConversationSession();
        var b = new ConversationSession();

        Assert.NotEqual(a.PublicId, b.PublicId);
    }

    [Fact]
    public void AddAssistantMessage_TextOnly_HasNoPendingCalls()
    {
        var session = new ConversationSession();
        session.AddUserMessage("привет");

        var message = session.AddAssistantMessage("здравствуй!", Array.Empty<ToolCall>());

        Assert.Equal(MessageRole.Assistant, message.Role);
        Assert.Equal("здравствуй!", message.Text);
        Assert.Empty(session.PendingToolCalls);
    }

    [Fact]
    public void AddAssistantMessage_NoTextAndNoCalls_Throws()
    {
        var session = new ConversationSession();
        session.AddUserMessage("привет");

        Assert.Throws<ArgumentException>(
            () => session.AddAssistantMessage("  ", Array.Empty<ToolCall>()));
    }

    [Fact]
    public void AddAssistantMessage_DuplicateCallIds_Throws()
    {
        var session = new ConversationSession();
        session.AddUserMessage("привет");

        Assert.Throws<ArgumentException>(
            () => session.AddAssistantMessage(null, new[] { CallA, CallA with { Name = "other" } }));
    }

    [Fact]
    public void AddAssistantMessage_SeveralCalls_AllPending()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");

        var message = session.AddAssistantMessage(null, new[] { CallA, CallB });

        Assert.Null(message.Text);
        Assert.Equal(new[] { CallA, CallB }, message.ToolCalls);
        Assert.Equal(new[] { CallA, CallB }, session.PendingToolCalls);
    }

    [Fact]
    public void AddToolResult_AnswersCallsOneByOne()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage("Сейчас посмотрю", new[] { CallA, CallB });

        var result = session.AddToolResult("call_b", "users, orders");

        Assert.Equal(MessageRole.Tool, result.Role);
        Assert.Equal("call_b", result.ToolCallId);
        Assert.Equal("users, orders", result.Text);
        Assert.Equal(new[] { CallA }, session.PendingToolCalls);

        session.AddToolResult("call_a", "1");
        Assert.Empty(session.PendingToolCalls);
    }

    [Fact]
    public void AddToolResult_UnknownId_Throws()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage(null, new[] { CallA });

        Assert.Throws<InvalidOperationException>(
            () => session.AddToolResult("call_zzz", "?"));
    }

    [Fact]
    public void AddToolResult_SameIdTwice_Throws()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage(null, new[] { CallA, CallB });
        session.AddToolResult("call_a", "1");

        Assert.Throws<InvalidOperationException>(
            () => session.AddToolResult("call_a", "1"));
    }

    [Fact]
    public void AddToolResult_ForOlderAssistantCall_Throws()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage(null, new[] { CallA });
        session.AddToolResult("call_a", "1");
        session.AddAssistantMessage(null, new[] { CallB });

        // call_a принадлежит предыдущему ответу и уже закрыт.
        Assert.Throws<InvalidOperationException>(
            () => session.AddToolResult("call_a", "1"));
    }

    [Fact]
    public void AddUserMessage_WhilePendingCalls_Throws()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage(null, new[] { CallA, CallB });
        session.AddToolResult("call_a", "1");

        Assert.Throws<InvalidOperationException>(
            () => session.AddUserMessage("ну что там?"));
    }

    [Fact]
    public void AddUserMessage_AfterAllResults_Succeeds()
    {
        var session = new ConversationSession();
        session.AddUserMessage("что в базе?");
        session.AddAssistantMessage(null, new[] { CallA, CallB });
        session.AddToolResult("call_a", "1");
        session.AddToolResult("call_b", "users");
        session.AddAssistantMessage("В базе две таблицы.", Array.Empty<ToolCall>());

        session.AddUserMessage("спасибо");

        Assert.Equal(6, session.Messages.Count);
    }
}
