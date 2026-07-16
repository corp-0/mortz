using Mortz.Core.Chat.Commands;
using Xunit;

namespace Mortz.Tests.Core.Chat;

public class ChatCommandTests
{
    private sealed class RecordingContext
    {
        public string? Executed;
    }

    private sealed class TestAdminCommand : ChatCommand<RecordingContext>
    {
        public string Password { get; private set; } = "";
        public string Note { get; private set; } = "";

        public override bool TryBind(IReadOnlyList<string> arguments, out string error)
        {
            if (arguments is not [var password, var note])
            {
                error = "Usage: /admin <password> <note>";
                return false;
            }
            Password = password;
            Note = note;
            error = "";
            return true;
        }

        public override void Execute(RecordingContext context) =>
            context.Executed = $"{Password}|{Note}";
    }

    private static ChatCommandRegistry<RecordingContext> CreateRegistry()
    {
        ChatCommandRegistry<RecordingContext> registry = new();
        registry.Register(
            new ChatCommandMetadata(new ChatCommandName("admin"), "/admin <password> <note>",
                "Authenticate.", [new ChatCommandName("auth")], Sensitive: true),
            static () => new TestAdminCommand());
        return registry;
    }

    [Fact]
    public void Commands_BindTextToTypedSensitiveCommand()
    {
        ChatCommandRegistry<RecordingContext> registry = CreateRegistry();

        Assert.True(registry.TryParse("/ADMIN \"two words\" escaped\\ value",
            out ChatCommand<RecordingContext>? parsed, out _));
        TestAdminCommand command = Assert.IsType<TestAdminCommand>(parsed);
        Assert.Equal("two words", command.Password);
        Assert.Equal("escaped value", command.Note);
        Assert.True(Assert.Single(registry.Commands).Sensitive);

        Assert.True(registry.TryParse("/auth secret note", out parsed, out _));
        Assert.IsType<TestAdminCommand>(parsed);
        Assert.False(registry.TryParse("/missing", out _, out string error));
        Assert.Contains("Unknown command", error);
    }

    [Fact]
    public void Commands_CreateFreshInstancesThatExecuteAgainstTheContext()
    {
        ChatCommandRegistry<RecordingContext> registry = CreateRegistry();

        Assert.True(registry.TryParse("/admin one two",
            out ChatCommand<RecordingContext>? first, out _));
        Assert.True(registry.TryParse("/admin three four",
            out ChatCommand<RecordingContext>? second, out _));
        Assert.NotSame(first, second);

        RecordingContext context = new();
        second!.Execute(context);
        Assert.Equal("three|four", context.Executed);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin \"unterminated")]
    [InlineData("/bad!name")]
    public void Commands_RejectMalformedInput(string input)
    {
        ChatCommandRegistry<RecordingContext> registry = CreateRegistry();
        Assert.False(registry.TryParse(input, out _, out _));
    }
}
