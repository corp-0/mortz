using Mortz.Core.Match;
using Xunit;

namespace Mortz.Tests.Core.Match;

public class MatchPointTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AWonMatchIsNotMatchPoint(int remaining) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MatchPoint(remaining, new PlayerVictor(1)));

    [Fact]
    public void HoldingItWithoutALeaderIsFine()
    {
        MatchPoint held = new MatchPoint(1, null);

        Assert.Equal(1, held.Remaining);
        Assert.Null(held.Leader);
    }
}
