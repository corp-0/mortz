using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Net.Stats;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Mortz.Server.Match;
using Mortz.Server.Match.Services;
using Mortz.Server.Players;
using Mortz.Server.Wins;
using Serilog.Core;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class MatchWinRecorderTests
{
    [Fact]
    public void RecordsTheCompletedMatchWinner()
    {
        var transport = new RecordingTransport();
        var serverKeys = new ServerStateKeys(generation: 1);
        var roster = new Roster(serverKeys);
        var wins = new WinsService(serverKeys, roster, new ReadyLink(transport), Logger.None);
        var matchKeys = new MatchStateKeys(generation: 2);
        using MatchRuntime runtime = new(
            new TerrainMask(128, 128, (_, _) => false, (_, _) => false),
            new MatchConfig
            {
                Rules = new ModeRules
                {
                    Victory = new KillsVictoryRules { Target = 1 },
                    SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
                    SpawnImmunity = 0,
                },
            },
            victoryLapTicks: 3,
            matchKeys,
            [new SpawnPoint(new Vec2(10, 32)), new SpawnPoint(new Vec2(40, 32))]);
        Player winner = roster.Join(1, "winner");
        Player victim = roster.Join(2, "victim");
        winner.OpenMatch(matchKeys.Count, matchKeys.Generation);
        victim.OpenMatch(matchKeys.Count, matchKeys.Generation);
        runtime.Seat(winner);
        runtime.Seat(victim);
        runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);
        MatchUpdate update = runtime.Advance(default);

        new MatchWinRecorder(runtime, wins).MatchUpdated(update, default);

        SessionWinsMsg message = transport.Messages
            .Select(sent => sent.Message)
            .OfType<SessionWinsMsg>()
            .Last();
        Assert.Equal(1, message.Rows.Single(row => row.PeerId == winner.PeerId).Wins);
        Assert.Equal(0, message.Rows.Single(row => row.PeerId == victim.PeerId).Wins);
    }
}
