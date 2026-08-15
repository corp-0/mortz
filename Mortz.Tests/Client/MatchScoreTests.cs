using Mortz.Client.Match;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Score;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class MatchScoreTests : NodeServiceTest
{
    private ClientMatchState HostState(int generation = 3)
    {
        ClientMatchState state = new(generation, MatchParticipation.Active);
        ClientMatchStateAdapter adapter = new();
        adapter.Initialize(state);
        HostRouted(adapter);
        return state;
    }

    [Fact]
    public void SyncSeedReplacesEverything()
    {
        ClientMatchState state = HostState();
        new ScoreSyncMsg([new ScoreRow(7, 9, 9), new ScoreRow(9, 8, 7)], 9, 9).SendTo(Router, 1);

        new ScoreSyncMsg([new ScoreRow(7, 3, 0), new ScoreRow(8, 1, 2)], 4, 2).SendTo(Router, 1);

        Assert.Equal(3, state.Scores.Kills(7));
        Assert.Equal(1, state.Scores.Kills(8));
        Assert.Equal(0, state.Scores.Deaths(7));
        Assert.Equal(2, state.Scores.Deaths(8));
        Assert.Equal(0, state.Scores.Kills(9));
        Assert.Equal(0, state.Scores.Deaths(9));
        Assert.Equal(4, state.Scores.TeamKills[Team.BLUE]);
        Assert.Equal(2, state.Scores.TeamKills[Team.RED]);
    }

    [Fact]
    public void EliminationsPatchTheAffectedRows()
    {
        ClientMatchState state = HostState();
        new ScoreSyncMsg(
            [new ScoreRow(7, 3, 0), new ScoreRow(8, 1, 2), new ScoreRow(9, 6, 5)],
            4, 2).SendTo(Router, 1);
        MatchScoreSnapshot before = state.Scores;

        new EliminationMsg(7, 8, EliminationFlags.NONE, 4, 3, 0, 0, 5, 2).Broadcast(Router);

        Assert.Equal(4, state.Scores.Kills(7));
        Assert.Equal(3, state.Scores.Deaths(8));
        Assert.Equal(1, state.Scores.Kills(8));
        Assert.Equal(6, state.Scores.Kills(9));
        Assert.Equal(5, state.Scores.Deaths(9));
        Assert.Equal(5, state.Scores.TeamKills[Team.BLUE]);
        Assert.Equal(2, state.Scores.TeamKills[Team.RED]);
        Assert.Equal(3, before.Kills(7));
        Assert.Equal(2, before.Deaths(8));
        Assert.Equal(4, before.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void SuicidePenaltyLandsOnTheVictim()
    {
        ClientMatchState state = HostState();
        new ScoreSyncMsg([new ScoreRow(8, 2, 0)], 2, 0).SendTo(Router, 1);

        new EliminationMsg(0, 8, EliminationFlags.SUICIDE | EliminationFlags.FALL,
            1, 1, 0, 0, 1, 0).Broadcast(Router);

        Assert.Equal(1, state.Scores.Kills(8));
        Assert.Equal(1, state.Scores.Deaths(8));
        Assert.Equal(1, state.Scores.TeamKills[Team.BLUE]);
    }

    [Fact]
    public void RewardedSuicideKillLandsOnTheEnemy()
    {
        ClientMatchState state = HostState();
        new ScoreSyncMsg([new ScoreRow(7, 2, 0), new ScoreRow(8, 0, 0)], 0, 0).SendTo(Router, 1);

        new EliminationMsg(8, 8, EliminationFlags.SUICIDE,
            0, 1, 7, 3, 0, 0).Broadcast(Router);

        Assert.Equal(3, state.Scores.Kills(7));
        Assert.Equal(0, state.Scores.Kills(8));
        Assert.Equal(1, state.Scores.Deaths(8));
    }
}
