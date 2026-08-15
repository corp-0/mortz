using Mortz.Client.Players;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class ClientPlayersTests : NodeServiceTest
{
    private sealed class Counter
    {
        public int Value { get; set; }
    }

    private static RosterEntry Entry(int peerId, string name, byte skin = 0,
        Team? team = null, byte slot = 1) => new(peerId, name, skin, team, slot);

    private static LobbyStateMsg Lobby(params LobbyMember[] members) => new(members, []);

    [Fact]
    public void TheMatchStreamReplacesTheRoster()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        new RosterMsg([Entry(1, "Alice", slot: 1), Entry(2, "Bob", slot: 2)]).Broadcast(Router);

        new RosterMsg([Entry(2, "Bobby", skin: 3, team: Team.RED, slot: 1)]).Broadcast(Router);

        Assert.Null(players.Find(1));
        ClientPlayer bobby = players.Find(2)!;
        Assert.Equal("Bobby", bobby.Name);
        Assert.Equal(3, bobby.Skin);
        Assert.Equal(Team.RED, bobby.Team);
        Assert.Equal(2, players.Table.PeerInSlot(new NetSlot(1)));
    }

    [Fact]
    public void TheLobbyStreamFeedsIdentityAndRetiresLeavers()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        Lobby(new LobbyMember(1, "Alice", true, Team.BLUE),
            new LobbyMember(2, "Bob", false, Team.RED)).Broadcast(Router);

        ClientPlayer alice = players.Find(1)!;
        Assert.Equal("Alice", alice.Name);
        Assert.True(alice.Ready);
        Assert.Equal(Team.BLUE, alice.Team);

        Lobby(new LobbyMember(2, "Bob", false, Team.RED)).Broadcast(Router);

        Assert.Null(players.Find(1));
        Assert.Single(players);
    }

    [Fact]
    public void EachStreamKeepsWhatItDoesNotCarry()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());

        Lobby(new LobbyMember(1, "Alice", true, Team.BLUE)).Broadcast(Router);
        new RosterMsg([Entry(1, "Alice", skin: 5, team: Team.BLUE)]).Broadcast(Router);
        ClientPlayer alice = players.Find(1)!;
        Assert.True(alice.Ready);
        Assert.Equal(5, alice.Skin);

        Lobby(new LobbyMember(1, "Alice", false, Team.BLUE)).Broadcast(Router);
        Assert.False(alice.Ready);
        Assert.Equal(5, alice.Skin);
    }

    [Fact]
    public void AnEarlyReferencedPlayerIsSparedUntilConfirmed()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        SessionStateKey<Counter> counter = players.SessionKeys.Claim<Counter>();
        players.GetOrCreate(7).State(counter).Value = 42;

        new RosterMsg([Entry(1, "Alice")]).Broadcast(Router);
        Assert.NotNull(players.Find(7));

        new RosterMsg([Entry(1, "Alice", slot: 1), Entry(7, "Grace", slot: 2)]).Broadcast(Router);
        ClientPlayer grace = players.Find(7)!;
        Assert.Equal("Grace", grace.Name);
        Assert.Equal(42, grace.State(counter).Value);

        new RosterMsg([Entry(1, "Alice")]).Broadcast(Router);
        Assert.Null(players.Find(7));
    }

    // The marker is deliberately unlike the server's "Player 42" fallback for a
    // blank name, so a nameplate showing it means no broadcast has landed.
    [Fact]
    public void AnUnknownPeerGetsAMarkerNoRealNameCanMatch()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        Assert.Equal("<unknown 42>", players.NameOf(42));
        Assert.NotEqual("Player 42", players.NameOf(42));
        Assert.Null(players.TeamOf(42));
        Assert.Null(players.Find(42));
    }

    [Fact]
    public void ChangedFiresOncePerBroadcastAndOnlyOnRealChanges()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        int changed = 0;
        players.Changed += () => changed++;

        new RosterMsg([Entry(1, "Alice", slot: 1), Entry(2, "Bob", slot: 2)]).Broadcast(Router);
        Assert.Equal(1, changed);

        new RosterMsg([Entry(1, "Alice", slot: 1), Entry(2, "Bob", slot: 2)]).Broadcast(Router);
        Assert.Equal(1, changed);

        new RosterMsg([Entry(1, "Alice", slot: 1), Entry(2, "Bobby", slot: 2)]).Broadcast(Router);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void ADuplicateSlotLeavesThePreviousStateIntact()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        new RosterMsg([Entry(1, "Alice")]).Broadcast(Router);
        int changed = 0;
        players.Changed += () => changed++;

        new RosterMsg([Entry(1, "Alice", slot: 1), Entry(2, "Bob", slot: 1)]).Broadcast(Router);

        Assert.Equal("Alice", players.NameOf(1));
        Assert.Null(players.Find(2));
        Assert.Equal(0, changed);
    }

    [Fact]
    public void JoinAndLeaveEventsBracketThePlayersLifetime()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        SessionStateKey<Counter> counter = players.SessionKeys.Claim<Counter>();
        List<int> joined = [];
        int lastSeen = 0;
        players.PlayerJoined += player => joined.Add(player.PeerId);
        // Leave subscribers get their last look at the player's state.
        players.PlayerLeft += player => lastSeen = player.State(counter).Value;

        new RosterMsg([Entry(1, "Alice")]).Broadcast(Router);
        players.Find(1)!.State(counter).Value = 9;

        new RosterMsg([Entry(2, "Bob")]).Broadcast(Router);

        Assert.Equal([1, 2], joined);
        Assert.Equal(9, lastSeen);
    }

    [Fact]
    public void AKeyFromAnotherRegistryIsStale()
    {
        ClientPlayers first = HostRouted(new ClientPlayers());
        ClientPlayers second = HostRouted(new ClientPlayers());
        SessionStateKey<Counter> foreign = first.SessionKeys.Claim<Counter>();

        Assert.Throws<InvalidOperationException>(() => second.GetOrCreate(1).State(foreign));
    }

    [Fact]
    public void MatchSnapshotUpdatesCanonicalPresentationWithoutRegressingOnLatePackets()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());

        players.ApplySnapshot(Snapshot(12, 7));
        players.ApplySnapshot(Snapshot(10, 5));

        Assert.Equal(7, players.Find(1)!.Match!.LatestPresentation.KillingSpreeMagnitude);
    }

    [Fact]
    public void OpeningTheNextMatchReplacesCanonicalMatchState()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        players.ApplySnapshot(Snapshot(12, 7));

        players.OpenMatch(new MatchConfig());

        Assert.Equal(default, players.Find(1)!.Match!.LatestPresentation);
    }

    [Fact]
    public void TypingBroadcastUpdatesThePlayersConnectionState()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());

        new TypingStateMsg(7, true).Broadcast(Router);
        Assert.True(players.Find(7)!.IsTyping);

        new TypingStateMsg(7, false).Broadcast(Router);
        Assert.False(players.Find(7)!.IsTyping);
    }

    [Fact]
    public void LeavingAndRejoiningCreatesFreshCanonicalMatchState()
    {
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        new RosterMsg([Entry(1, "Alice")]).Broadcast(Router);
        players.ApplySnapshot(Snapshot(12, 7));

        new RosterMsg([Entry(2, "Bob")]).Broadcast(Router);
        new RosterMsg([Entry(1, "Alice")]).Broadcast(Router);

        Assert.Equal(default, players.Find(1)!.Match!.LatestPresentation);
    }

    private static MatchSnapshot Snapshot(int tick, byte magnitude) => new(
        tick,
        [
            new ReplicatedPlayer(
                new PlayerState { PeerId = 1 },
                new PlayerPresentationState { KillingSpreeMagnitude = magnitude }),
        ]);
}
