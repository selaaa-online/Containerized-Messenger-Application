using Shared;
using Xunit;

namespace Shared.Tests;

public class MessageTests
{
    [Fact]
    public void ToJson_FromJson_RoundTripsAllFields()
    {
        var original = Message.PrivateMessage("alice", "bob", "hello");

        var restored = Message.FromJson(original.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(MessageType.Private, restored!.Type);
        Assert.Equal("alice", restored.Username);
        Assert.Equal("bob", restored.To);
        Assert.Equal("hello", restored.Text);
    }

    [Fact]
    public void ToJson_OmitsNullFields()
    {
        var json = Message.Chat("alice", "hi").ToJson();

        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("\"To\"", json);
    }

    [Fact]
    public void ToJson_SerializesTypeAsString()
    {
        var json = Message.Chat("alice", "hi").ToJson();

        Assert.Contains("\"Chat\"", json);
        Assert.DoesNotContain("\"Type\":2", json);
    }

    [Fact]
    public void Auth_SetsUsernameAndPassword()
    {
        var message = Message.Auth("alice", "secret");

        Assert.Equal(MessageType.Auth, message.Type);
        Assert.Equal("alice", message.Username);
        Assert.Equal("secret", message.Password);
    }

    [Theory]
    [InlineData(true, "welcome")]
    [InlineData(false, "denied")]
    public void AuthResult_SetsSuccessAndText(bool success, string text)
    {
        var message = Message.AuthResult(success, text);

        Assert.Equal(MessageType.AuthResult, message.Type);
        Assert.Equal(success, message.Success);
        Assert.Equal(text, message.Text);
    }

    [Fact]
    public void Chat_SetsUsernameAndText()
    {
        var message = Message.Chat("alice", "hello world");

        Assert.Equal(MessageType.Chat, message.Type);
        Assert.Equal("alice", message.Username);
        Assert.Equal("hello world", message.Text);
    }

    [Fact]
    public void UserListRequest_HasNoBody()
    {
        var message = Message.UserListRequest();

        Assert.Equal(MessageType.UserList, message.Type);
        Assert.Null(message.Text);
    }

    [Fact]
    public void UserListResult_CarriesTextPayload()
    {
        var message = Message.UserListResult("alice, bob");

        Assert.Equal(MessageType.UserList, message.Type);
        Assert.Equal("alice, bob", message.Text);
    }

    [Fact]
    public void System_SetsText()
    {
        var message = Message.System("server restarting");

        Assert.Equal(MessageType.System, message.Type);
        Assert.Equal("server restarting", message.Text);
    }

    [Fact]
    public void Timestamp_DefaultsToUtcNow()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var message = Message.Chat("alice", "hi");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(message.Timestamp, before, after);
    }
}
