using Chickensoft.AutoInject;
using Mortz.Client.Players;
using Mortz.Client.Score;
using Mortz.Core.Match;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Score;
using Mortz.Net;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class MatchScoreTests : NodeServiceTest
{
    private MatchScore HostScore()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        MatchScore score = new();
        score.FakeDependency(players);
        return HostRouted(score);
    }

    [Fact]
    public void SyncSeedReplacesEverything()
    {
        MatchScore score = HostScore();
        new ScoreSyncMsg([new ScoreRow(7, 9, 9)], 9, 9).SendTo(1);

        new ScoreSyncMsg([new ScoreRow(7, 3, 0), new ScoreRow(8, 1, 2)], 4, 2).SendTo(1);

        Assert.Equal(3, score.Kills(7));
        Assert.Equal(1, score.Kills(8));
        Assert.Equal(0, score.Deaths(7));
        Assert.Equal(2, score.Deaths(8));
        Assert.Equal(4, score.TeamKills(Team.BLUE));
        Assert.Equal(2, score.TeamKills(Team.RED));
    }

    [Fact]
    public void EliminationsPatchTheAffectedRows()
    {
        MatchScore score = HostScore();
        new ScoreSyncMsg([new ScoreRow(7, 3, 0), new ScoreRow(8, 1, 2)], 4, 2).SendTo(1);

        new EliminationMsg(7, 8, EliminationFlags.NONE, 4, 3, 0, 0, 5, 2).Broadcast();

        Assert.Equal(4, score.Kills(7));
        Assert.Equal(3, score.Deaths(8));
        Assert.Equal(1, score.Kills(8));
        Assert.Equal(5, score.TeamKills(Team.BLUE));
        Assert.Equal(2, score.TeamKills(Team.RED));
    }

    [Fact]
    public void SuicidePenaltyLandsOnTheVictim()
    {
        MatchScore score = HostScore();
        new ScoreSyncMsg([new ScoreRow(8, 2, 0)], 2, 0).SendTo(1);

        new EliminationMsg(0, 8, EliminationFlags.SUICIDE | EliminationFlags.FALL,
            1, 1, 0, 0, 1, 0).Broadcast();

        Assert.Equal(1, score.Kills(8));
        Assert.Equal(1, score.Deaths(8));
        Assert.Equal(1, score.TeamKills(Team.BLUE));
    }

    [Fact]
    public void RewardedSuicideKillLandsOnTheEnemy()
    {
        MatchScore score = HostScore();
        new ScoreSyncMsg([new ScoreRow(7, 2, 0), new ScoreRow(8, 0, 0)], 0, 0).SendTo(1);

        new EliminationMsg(8, 8, EliminationFlags.SUICIDE,
            0, 1, 7, 3, 0, 0).Broadcast();

        Assert.Equal(3, score.Kills(7));
        Assert.Equal(0, score.Kills(8));
        Assert.Equal(1, score.Deaths(8));
    }
}
