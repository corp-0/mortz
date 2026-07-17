using Mortz.Client.Score;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class MatchScoreTests : NodeServiceTest
{
    [Fact]
    public void SyncSeedReplacesEverything()
    {
        MatchScore score = Host(new MatchScore());
        new ScoreSyncMsg([7], [9], [9], 9, 9).SendTo(1);

        new ScoreSyncMsg([7, 8], [3, 1], [0, 2], 4, 2).SendTo(1);

        Assert.Equal(3, score.Kills(7));
        Assert.Equal(1, score.Kills(8));
        Assert.Equal(0, score.Deaths(7));
        Assert.Equal(2, score.Deaths(8));
        Assert.Equal(4, score.TeamKills(1));
        Assert.Equal(2, score.TeamKills(2));
    }

    [Fact]
    public void EliminationsPatchTheAffectedRows()
    {
        MatchScore score = Host(new MatchScore());
        new ScoreSyncMsg([7, 8], [3, 1], [0, 2], 4, 2).SendTo(1);

        new EliminationMsg(7, 8, EliminationFlags.NONE, 4, 3, 5, 2).Broadcast();

        Assert.Equal(4, score.Kills(7));
        Assert.Equal(3, score.Deaths(8));
        Assert.Equal(1, score.Kills(8));
        Assert.Equal(5, score.TeamKills(1));
        Assert.Equal(2, score.TeamKills(2));
    }

    [Fact]
    public void SuicidePenaltyLandsOnTheVictim()
    {
        MatchScore score = Host(new MatchScore());
        new ScoreSyncMsg([8], [2], [0], 2, 0).SendTo(1);

        new EliminationMsg(0, 8, EliminationFlags.SUICIDE | EliminationFlags.FALL,
            1, 1, 1, 0).Broadcast();

        Assert.Equal(1, score.Kills(8));
        Assert.Equal(1, score.Deaths(8));
        Assert.Equal(1, score.TeamKills(1));
    }

    [Fact]
    public void TransportResetClearsAndNotifies()
    {
        MatchScore score = Host(new MatchScore());
        new ScoreSyncMsg([7], [3], [1], 3, 0).SendTo(1);
        int changes = 0;
        score.Changed += () => changes++;

        NetworkManager.Instance.ResetPeer();

        Assert.Equal(0, score.Kills(7));
        Assert.Equal(0, score.Deaths(7));
        Assert.Equal(0, score.TeamKills(1));
        Assert.Equal(1, changes);
    }
}
