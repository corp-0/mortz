using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Session;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Net.Roster;
using Mortz.Protocol.Net.Stats;
using Mortz.Protocol.Replication;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class RuntimeScopeTests
{
    private class Sender : IClientSender, ISessionExit
    {
        public void Send<T>(in T message) where T : struct, INetMessage<T> { }
        public void LeaveSession(string reason) { }
    }

    [Fact]
    public void ConnectionClosureStopsDeliveryAndRetiresPlayerState()
    {
        NetRouter router = new();
        Sender sender = new();
        ClientConnectionScope session = new(router, sender, () => 1, sender);
        PingUpdateMsg update = new([new PeerPing(1, 42)]);
        Assert.True(router.Dispatch(PingUpdateMsg.MsgId, PingUpdateMsg.Serialize(update)));
        ClientPlayer player = session.Players.Find(1)!;
        Assert.Equal(42, session.Pings.Of(player));
        session.Dispose();
        Assert.False(router.Dispatch(PingUpdateMsg.MsgId, PingUpdateMsg.Serialize(update)));
        Assert.Throws<ObjectDisposedException>(() => session.Pings.Of(player));
    }

    [Fact]
    public void MatchClosureKeepsSessionWinsAndRetiresAllMatchState()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        session.Wins.Handle(new SessionWinsMsg([new PeerWins(1, 3)]));
        ClientMatchRuntime match = session.OpenMatch(new ClientMatchState(7, MatchParticipation.Active),
            EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        ClientPlayer player = session.Players.Find(1)!;
        Assert.NotNull(player.Match);
        session.CloseMatch();
        Assert.Null(player.Match);
        Assert.Equal(3, session.Wins.Of(player));
        Assert.False(router.Dispatch(MatchEndMsg.MsgId,
            MatchEndMsg.Serialize(new MatchEndMsg(false, 1, MatchGeneration: 7))));
        Assert.Null(match.State.Winner);
    }

    [Fact]
    public void OldMatchPacketsAndReusedSlotsCannotMutateTheCurrentMatch()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        ClientMatchRuntime match = session.OpenMatch(new ClientMatchState(7, MatchParticipation.Active),
            EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        session.Players.Handle(new RosterMsg([new RosterEntry(1, "player", 0, null, 1)], Revision: 2));
        MatchSnapshot current = new(1,
            [new ReplicatedPlayer(new PlayerState { PeerId = 1, Health = 100 }, default)],
            Generation: 7, RosterRevision: 2);
        Assert.True(match.AcceptSnapshot(current.SerializeFor(1), -1));
        Assert.False(match.AcceptSnapshot((current with { Tick = 900, Generation = 6 }).SerializeFor(1), 800));
        Assert.False(match.AcceptSnapshot((current with { Tick = 901, RosterRevision = 1 }).SerializeFor(1), 800));
        MatchEndMsg stale = new(false, 1, MatchGeneration: 6);
        Assert.False(router.Dispatch(MatchEndMsg.MsgId, MatchEndMsg.Serialize(stale)));
        Assert.Null(match.State.Winner);
        Assert.Equal(1, match.Interpolator.NewestTick);
    }

    [Fact]
    public void ScopeRejectsDuplicateRegistrationAndLateStateClaims()
    {
        using FeatureScope scope = new(_ => { }, _ => { });
        object feature = scope.Register(new object());
        Assert.Throws<InvalidOperationException>(() => scope.Register(feature));
        SessionStateKeys keys = new(1);
        keys.Seal();
        Assert.Throws<InvalidOperationException>(() => keys.Claim<object>());
    }

    [Fact]
    public void InitialSnapshotSeedsTheMatchBeforeItsRosterAndCannotBeReapplied()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        ClientMatchRuntime match = session.OpenMatch(new ClientMatchState(7, MatchParticipation.Active),
            EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        MatchSnapshot initial = new(0,
            [new ReplicatedPlayer(new PlayerState { PeerId = 1, Health = 100 }, default),
             new ReplicatedPlayer(new PlayerState { PeerId = 2, Health = 100 }, default)],
            Generation: 7, RosterRevision: 1);

        Assert.False(match.InitializeSnapshot(initial.SerializeFor(1), -1));
        Assert.False(match.InitializeSnapshot((initial with { Generation = 6 }).Serialize(), -1));
        Assert.True(match.InitializeSnapshot(initial.Serialize(), -1));
        Assert.Equal(0, match.Interpolator.NewestTick);
        Assert.NotNull(session.Players.Find(2)?.Match);
        Assert.False(match.InitializeSnapshot((initial with { Tick = 100 }).Serialize(), -1));

        MatchSnapshot live = initial with { Tick = 2 };
        Assert.False(match.AcceptSnapshot(live.SerializeFor(1), -1));
        session.Players.Handle(new RosterMsg(
            [new RosterEntry(1, "local", 0, null, 1), new RosterEntry(2, "remote", 0, null, 2)], Revision: 1));
        Assert.True(match.AcceptSnapshot(live.SerializeFor(1), -1));
        Assert.False(match.AcceptSnapshot([.. live.SerializeFor(1), 0], -1));
        Assert.False(match.AcceptSnapshot([0], -1));
        Assert.Equal(2, match.Interpolator.NewestTick);
    }

    private static TerrainMask EmptyTerrain() => new(64, 64, (_, _) => false, (_, _) => false);
}
