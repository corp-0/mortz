using Mortz.Core;
using Xunit;

namespace Mortz.Tests.Core;

public class ChatPolicyTests
{
    private abstract record TestChatCommand : IChatCommand;
    private sealed record TestAdminCommand(string Password, string Note) : TestChatCommand,
        ISensitiveChatCommand;

    private sealed class TestAdminCommandDefinition :
        ChatCommandDefinition<TestChatCommand, TestAdminCommand>
    {
        public TestAdminCommandDefinition() : base(new ChatCommandName("admin"),
            "/admin <password> <note>", "Authenticate.", new ChatCommandName("auth"))
        {
        }

        public override bool TryBind(IReadOnlyList<string> arguments,
            out TestAdminCommand? command, out string error)
        {
            command = arguments.Count == 2
                ? new TestAdminCommand(arguments[0], arguments[1])
                : null;
            error = command == null ? "Usage: /admin <password> <note>" : "";
            return command != null;
        }
    }

    [Fact]
    public void State_PersistsStructuredEntriesAndEvictsOldest()
    {
        ChatState state = new(capacity: 2);
        state.Add(new ChatEntry(ChatEntryKind.Player, 7, "Alice", "one"));
        state.AddSystem("two");
        state.AddSystem("three", isPrivate: true);

        Assert.Equal(2, state.Entries.Count);
        Assert.Equal("two", state.Entries[0].Text);
        Assert.Equal(ChatEntryKind.Private, state.Entries[1].Kind);
    }

    [Fact]
    public void Text_RemovesUnsafeRunesAndPreservesUnicode()
    {
        Assert.True(ChatTextSanitizer.TrySanitize("  hello\r\n\u202E🙂  ",
            out string text, out ChatRejectReason reason));
        Assert.Equal(ChatRejectReason.None, reason);
        Assert.Equal("hello🙂", text);
    }

    [Fact]
    public void Text_RejectsEmptyCommandsAndUtf8OversizeWithoutSplittingRunes()
    {
        Assert.False(ChatTextSanitizer.TrySanitize("\r\n\u202E", out _, out ChatRejectReason empty));
        Assert.Equal(ChatRejectReason.Empty, empty);
        Assert.False(ChatTextSanitizer.TrySanitize("/admin secret", out _, out ChatRejectReason command));
        Assert.Equal(ChatRejectReason.Command, command);

        string exact = string.Concat(Enumerable.Repeat("🙂", NetConfig.MAX_CHAT_BYTES / 4));
        Assert.True(ChatTextSanitizer.TrySanitize(exact, out string accepted, out _));
        Assert.Equal(NetConfig.MAX_CHAT_BYTES, System.Text.Encoding.UTF8.GetByteCount(accepted));
        Assert.False(ChatTextSanitizer.TrySanitize(exact + "🙂", out _, out ChatRejectReason tooLong));
        Assert.Equal(ChatRejectReason.TooLong, tooLong);
    }

    [Fact]
    public void Policy_AllowsBurstThenRefillsAndCleansPeer()
    {
        ChatPolicy policy = new();
        for (int i = 0; i < 5; i++)
            Assert.True(policy.TryAccept(1, 1_000, $"line {i}", out _, out _));
        Assert.False(policy.TryAccept(1, 1_000, "blocked", out _, out ChatRejectReason limited));
        Assert.Equal(ChatRejectReason.RateLimited, limited);
        Assert.True(policy.TryAccept(1, 2_000, "refilled", out _, out _));
        policy.Remove(1);
        Assert.True(policy.TryAccept(1, 2_000, "fresh", out _, out _));
    }

    [Fact]
    public void Policy_InvalidMessagesAlsoConsumeAbuseBudget()
    {
        ChatPolicy policy = new();
        for (int i = 0; i < 5; i++)
            Assert.False(policy.TryAccept(1, 1_000, "/not-chat", out _, out _));
        Assert.False(policy.TryAccept(1, 1_000, "/not-chat", out _,
            out ChatRejectReason reason));
        Assert.Equal(ChatRejectReason.RateLimited, reason);
    }

    [Fact]
    public void Commands_BindTextToTypedSensitiveCommand()
    {
        ChatCommandRegistry<TestChatCommand> registry = new();
        registry.Register(new TestAdminCommandDefinition());

        Assert.True(registry.TryParse("/ADMIN \"two words\" escaped\\ value",
            out TestChatCommand? parsed, out _));
        TestAdminCommand command = Assert.IsType<TestAdminCommand>(parsed);
        Assert.Equal("two words", command.Password);
        Assert.Equal("escaped value", command.Note);
        Assert.IsAssignableFrom<ISensitiveChatCommand>(command);

        Assert.True(registry.TryParse("/auth secret note", out parsed, out _));
        Assert.IsType<TestAdminCommand>(parsed);
        Assert.False(registry.TryParse("/missing", out _, out string error));
        Assert.Contains("Unknown command", error);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin \"unterminated")]
    [InlineData("/bad!name")]
    public void Commands_RejectMalformedInput(string input)
    {
        ChatCommandRegistry<TestChatCommand> registry = new();
        registry.Register(new TestAdminCommandDefinition());
        Assert.False(registry.TryParse(input, out _, out _));
    }
}
