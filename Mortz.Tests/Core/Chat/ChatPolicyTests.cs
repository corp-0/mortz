using Mortz.Core;
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
}
