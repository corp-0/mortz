using Mortz.Core.Chat;
using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Chat;

public class ChatPolicyTests
{
    [Fact]
    public void Text_RemovesUnsafeRunesAndPreservesUnicode()
    {
        Assert.True(ChatTextSanitizer.TrySanitize("  hello\r\n\u202E🙂  ",
            out string text, out ChatRejectReason reason));
        Assert.Equal(ChatRejectReason.NONE, reason);
        Assert.Equal("hello🙂", text);
    }

    [Fact]
    public void Text_RejectsEmptyCommandsAndUtf8OversizeWithoutSplittingRunes()
    {
        Assert.False(ChatTextSanitizer.TrySanitize("\r\n\u202E", out _, out ChatRejectReason empty));
        Assert.Equal(ChatRejectReason.EMPTY, empty);
        Assert.False(ChatTextSanitizer.TrySanitize("/admin secret", out _, out ChatRejectReason command));
        Assert.Equal(ChatRejectReason.COMMAND, command);

        string exact = string.Concat(Enumerable.Repeat("🙂", NetConfig.MAX_CHAT_BYTES / 4));
        Assert.True(ChatTextSanitizer.TrySanitize(exact, out string accepted, out _));
        Assert.Equal(NetConfig.MAX_CHAT_BYTES, System.Text.Encoding.UTF8.GetByteCount(accepted));
        Assert.False(ChatTextSanitizer.TrySanitize(exact + "🙂", out _, out ChatRejectReason tooLong));
        Assert.Equal(ChatRejectReason.TOO_LONG, tooLong);
    }

    [Fact]
    public void Policy_AllowsBurstThenRefillsAndCleansPeer()
    {
        ChatPolicy policy = new();
        for (int i = 0; i < 5; i++)
            Assert.True(policy.TryAccept(1, 1_000, $"line {i}", out _, out _));
        Assert.False(policy.TryAccept(1, 1_000, "blocked", out _, out ChatRejectReason limited));
        Assert.Equal(ChatRejectReason.RATE_LIMITED, limited);
        Assert.True(policy.TryAccept(1, 2_000, "refilled", out _, out _));
        policy.Remove(1);
        Assert.True(policy.TryAccept(1, 2_000, "fresh", out _, out _));
    }

    [Fact]
    public void Policy_RollsShareTheChatBudget()
    {
        ChatPolicy policy = new();
        for (int i = 0; i < 5; i++)
            Assert.True(policy.TryAcceptRoll(1, 1_000));
        Assert.False(policy.TryAcceptRoll(1, 1_000));
        Assert.False(policy.TryAccept(1, 1_000, "also blocked", out _,
            out ChatRejectReason reason));
        Assert.Equal(ChatRejectReason.RATE_LIMITED, reason);
        Assert.True(policy.TryAcceptRoll(1, 2_000));
    }

    [Fact]
    public void Policy_InvalidMessagesAlsoConsumeAbuseBudget()
    {
        ChatPolicy policy = new();
        for (int i = 0; i < 5; i++)
            Assert.False(policy.TryAccept(1, 1_000, "/not-chat", out _, out _));
        Assert.False(policy.TryAccept(1, 1_000, "/not-chat", out _,
            out ChatRejectReason reason));
        Assert.Equal(ChatRejectReason.RATE_LIMITED, reason);
    }
}
