using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim.Modifiers;
using Mortz.Protocol.Net.Sim;
using Mortz.Protocol.Sim.Modifiers;
using Mortz.Server;
using Mortz.Server.Content;
using Mortz.Server.Match;
using Mortz.Server.Match.Services;
using Mortz.Server.Players;
using Serilog.Core;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class ModifierReplicationTests
{
    [Fact]
    public void MutationAndInitialSyncShareTheSameRevisionAndEffectiveTick()
    {
        Roster roster = new(new ServerStateKeys(1));
        Player player = roster.Join(1, "player");
        MatchStateKeys keys = new(3);
        MapSnapshot map = TestServer.Map("arena", "Arena");
        using MatchRuntime runtime = new(map.BuildMask(), new MatchConfig(), 10, keys);
        keys.Seal();
        player.OpenMatch(keys.Count, keys.Generation);
        runtime.Seat(player);
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        link.BeginLoading(1, 3, 0);
        link.Ready(1, 3);
        MatchReplication replication = new(runtime, roster, map, new TerrainHistory(),
            link, Logger.None, false);
        replication.RosterChanged();
        wire.Messages.Clear();

        runtime.World.AddModifier(1, new StatsModifier(ModifierId.SPECIAL,
            StatChange.Mul(Stat.MAX_RUN_SPEED, 2)));
        MatchUpdate update = runtime.Advance(default);
        replication.MatchUpdated(update, default);
        PlayerModifiersMsg changed = Assert.Single(wire.Messages.Select(sent => sent.Message)
            .OfType<PlayerModifiersMsg>());
        Assert.Equal(1, changed.Revision);
        Assert.Equal(1, changed.EffectiveTick);
        Assert.Equal(3, changed.MatchGeneration);
        Assert.Single(ModifierWire.Deserialize(changed.Modifiers));

        wire.Messages.Clear();
        runtime.Advance(default);
        replication.RosterChanged();
        PlayerModifiersMsg initial = wire.Last<PlayerModifiersMsg>();
        Assert.Equal(changed.Revision, initial.Revision);
        Assert.Equal(changed.EffectiveTick, initial.EffectiveTick);
        Assert.Equal(changed.Modifiers, initial.Modifiers);

        wire.Messages.Clear();
        runtime.World.RemoveModifier(1, ModifierId.SPECIAL);
        replication.MatchUpdated(runtime.Advance(default), default);
        PlayerModifiersMsg removed = wire.Last<PlayerModifiersMsg>();
        Assert.Equal(2, removed.Revision);
        Assert.Equal(3, removed.EffectiveTick);
        Assert.Empty(ModifierWire.Deserialize(removed.Modifiers));
        player.CloseMatch();
    }
}
