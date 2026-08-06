using Mortz.Core.Net.Lobby;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class LobbyMemberTests
{
    [Fact]
    public void ALobbyMemberNeedsAPositivePeerId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LobbyMember(0, "Alice", true, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LobbyMember(-1, "Alice", true, null));
    }
}
