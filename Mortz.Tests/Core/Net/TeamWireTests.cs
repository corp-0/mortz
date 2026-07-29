using Mortz.Core.Match;
using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class TeamWireTests
{
    [Fact]
    public void RealTeamsRoundTrip()
    {
        Assert.Equal(Team.BLUE, TeamWire.FromByte(TeamWire.ToByte(Team.BLUE)));
        Assert.Equal(Team.RED, TeamWire.FromByte(TeamWire.ToByte(Team.RED)));
    }

    [Fact]
    public void NoTeamIsZeroBothWays()
    {
        Assert.Equal(TeamWire.NONE, TeamWire.ToByte(null));
        Assert.Null(TeamWire.FromByte(TeamWire.NONE));
    }

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)200)]
    [InlineData(byte.MaxValue)]
    public void ATeamThatDoesNotExistDecodesToNoTeam(byte value) =>
        Assert.Null(TeamWire.FromByte(value));
}
