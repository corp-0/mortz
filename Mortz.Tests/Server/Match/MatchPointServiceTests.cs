using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Net.Match;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Mortz.Server.Match;
using Mortz.Server.Match.Services;
using Mortz.Server.Players;
using Serilog.Core;
using Xunit;

namespace Mortz.Tests.Server.Match;

public class MatchPointServiceTests : IDisposable
{
    private const int GENERATION = 2;

    private readonly RecordingTransport _transport = new();
    private readonly MatchRuntime _runtime;
    private readonly MatchPointService _matchPoint;
    private readonly Player _winner;
    private readonly Player _firstVictim;
    private readonly Player _secondVictim;

    public MatchPointServiceTests()
    {
        MatchStateKeys keys = new(GENERATION);
        _runtime = new MatchRuntime(
            new TerrainMask(128, 128, (_, _) => false, (_, _) => false),
            new MatchConfig
            {
                Rules = new ModeRules
                {
                    Victory = new KillsVictoryRules { Target = 2 },
                    SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
                    SpawnImmunity = 0,
                },
            },
            victoryLapTicks: 3,
            keys,
            [
                new SpawnPoint(new Vec2(10, 32)),
                new SpawnPoint(new Vec2(40, 32)),
                new SpawnPoint(new Vec2(80, 32)),
            ]);
        _winner = OpenPlayer(1, keys);
        _firstVictim = OpenPlayer(2, keys);
        _secondVictim = OpenPlayer(3, keys);
        _runtime.Seat(_winner);
        _runtime.Seat(_firstVictim);
        _runtime.Seat(_secondVictim);
        _matchPoint = new MatchPointService(new ReadyLink(_transport), Logger.None);
    }

    public void Dispose() => _runtime.Dispose();

    [Fact]
    public void PublishesOnlyEntryAndLapseTransitions()
    {
        MatchUpdate entered = Kill(_firstVictim);

        _matchPoint.MatchUpdated(entered, default);

        MatchPointMsg entry = _transport.Last<MatchPointMsg>();
        Assert.True(entry.Active);
        Assert.Equal(_winner.PeerId, entry.LeaderId);
        Assert.Equal(1, entry.Remaining);

        _transport.Messages.Clear();
        _matchPoint.MatchUpdated(_runtime.Advance(default), default);
        Assert.DoesNotContain(_transport.Messages, sent => sent.Message is MatchPointMsg);

        MatchUpdate won = Kill(_secondVictim);
        _matchPoint.MatchUpdated(won, default);

        Assert.False(_transport.Last<MatchPointMsg>().Active);
        Assert.Null(_matchPoint.Active);
    }

    [Fact]
    public void SyncsTheCurrentStateAfterLateJoinLoading()
    {
        _matchPoint.MatchUpdated(Kill(_firstVictim), default);
        _transport.Messages.Clear();
        Player jipPlayer = new(9, "jip", serverKeyCount: 0, serverGeneration: GENERATION);

        _matchPoint.Enter(jipPlayer, GENERATION, initialPhase: false);
        Assert.Empty(_transport.Messages);

        _matchPoint.Sync(jipPlayer);

        Sent sent = Assert.Single(_transport.Messages);
        Assert.Equal(jipPlayer.PeerId, sent.Target);
        Assert.True(Assert.IsType<MatchPointMsg>(sent.Message).Active);
    }

    private MatchUpdate Kill(Player victim)
    {
        _runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);
        return _runtime.Advance(default);
    }

    private static Player OpenPlayer(int peerId, MatchStateKeys keys)
    {
        Player player = new(peerId, $"player {peerId}", serverKeyCount: 0,
            serverGeneration: GENERATION);
        player.OpenMatch(keys.Count, keys.Generation);
        return player;
    }
}
