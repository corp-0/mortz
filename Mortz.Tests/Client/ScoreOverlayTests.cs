using Mortz.Client;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

public class ScoreOverlayTests
{
    private static string Name(long id) => id == 1 ? "Alice" : "Bob";

    [Fact]
    public void FormatsRegularElimination() =>
        Assert.Equal("Alice killed Bob", ScoreOverlay.FormatFeedLine(
            Message(EliminationFlags.NONE), Name));

    [Fact]
    public void FormatsFallAndSuicide()
    {
        Assert.Equal("Bob fell out of the world", ScoreOverlay.FormatFeedLine(
            Message(EliminationFlags.SUICIDE | EliminationFlags.FALL, killer: 0), Name));
        Assert.Equal("Bob blew themselves up", ScoreOverlay.FormatFeedLine(
            Message(EliminationFlags.SUICIDE, killer: 2), Name));
    }

    [Fact]
    public void OwnedTakesFeedPrecedence() =>
        Assert.Equal("Alice OWNED Bob", ScoreOverlay.FormatFeedLine(
            Message(EliminationFlags.OWNED), Name));

    [Fact]
    public void FormatsTeamKill() =>
        Assert.Equal("Alice team-killed Bob", ScoreOverlay.FormatFeedLine(
            Message(EliminationFlags.TEAM_KILL), Name));

    private static EliminationMsg Message(EliminationFlags flags, long killer = 1) =>
        new(killer, 2, flags, 3, 4, 5, 6);
}
