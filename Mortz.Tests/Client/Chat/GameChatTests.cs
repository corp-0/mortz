using Mortz.Client.Chat;
using Xunit;

namespace Mortz.Tests.Client.Chat;

public class GameChatTests
{
    [Fact]
    public void ClosedAlphaHoldsFullThenFadesThenGoes()
    {
        Assert.Equal(1f, GameChat.ClosedAlpha(0f));
        Assert.Equal(1f, GameChat.ClosedAlpha(7f));
        Assert.Equal(0.5f, GameChat.ClosedAlpha(7.5f), 0.001f);
        Assert.Equal(0f, GameChat.ClosedAlpha(8f));
        Assert.Equal(0f, GameChat.ClosedAlpha(float.MaxValue));
    }
}
