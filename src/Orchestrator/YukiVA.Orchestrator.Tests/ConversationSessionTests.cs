using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.Enums;

namespace YukiVA.Orchestrator.Tests;

public class ConversationSessionTests
{
    [Fact]
    public void AddMessage_AddsMessageToSession()
    {
        var session = new ConversationSession();        //Arrange
        session.AddMessage(MessageRole.User, "hello");  //Act
        Assert.Single(session.Messages);                //Assert
    }
    [Fact]
    public void AddMessage_SetsRoleAndText()
    {
        var session = new ConversationSession();
        var message = session.AddMessage(MessageRole.User, "how are you?");
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("how are you?", message.Text);
    }
    [Fact]
    public void AddMessage_EmptyText_ThrowsArgumentException()
    {
        var session = new ConversationSession();

        // Если исключения НЕ будет — тест упадёт.
        Assert.Throws<ArgumentException>(
            () => session.AddMessage(MessageRole.User, "   "));
    }
    [Fact]
    public void NewSession_HasUniquePublicId()
    {
        var a = new ConversationSession();
        var b = new ConversationSession();

        Assert.NotEqual(a.PublicId, b.PublicId);
    }
}
