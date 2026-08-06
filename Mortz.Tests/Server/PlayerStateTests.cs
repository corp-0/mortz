using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>The state key mechanism: a key resolves against the cells it was
/// claimed for, or it throws. Nothing in between.</summary>
public class PlayerStateTests
{
    private const int SERVER_GENERATION = 1;

    private sealed class Tally
    {
        public int Count;
    }

    private sealed class Note
    {
        public string Text = "";
    }

    [Fact]
    public void ACellIsCreatedOnFirstReadAndKeptAfterwards()
    {
        ServerStateKeys keys = new(SERVER_GENERATION);
        ServerStateKey<Tally> key = keys.Claim<Tally>();
        Player player = NewPlayer(keys);

        Tally first = player.State(key);
        first.Count = 3;

        Assert.Same(first, player.State(key));
        Assert.Equal(3, player.State(key).Count);
    }

    [Fact]
    public void EveryPlayerGetsTheirOwnCell()
    {
        ServerStateKeys keys = new(SERVER_GENERATION);
        ServerStateKey<Tally> key = keys.Claim<Tally>();
        Player one = NewPlayer(keys);
        Player two = NewPlayer(keys);

        one.State(key).Count = 5;

        Assert.Equal(0, two.State(key).Count);
    }

    [Fact]
    public void TwoKeysOfTheSameTypeAddressDifferentCells()
    {
        ServerStateKeys keys = new(SERVER_GENERATION);
        ServerStateKey<Tally> first = keys.Claim<Tally>();
        ServerStateKey<Tally> second = keys.Claim<Tally>();
        Player player = NewPlayer(keys);

        player.State(first).Count = 1;

        Assert.NotSame(player.State(first), player.State(second));
        Assert.Equal(0, player.State(second).Count);
    }

    [Fact]
    public void ADefaultKeyThrowsInEveryLifetime()
    {
        Player player = new(1, "one", 1, SERVER_GENERATION);
        player.OpenLobby(1, 2);

        Assert.Throws<InvalidOperationException>(() => player.State(default(ServerStateKey<Tally>)));
        Assert.Throws<InvalidOperationException>(() => player.State(default(LobbyStateKey<Tally>)));
        Assert.Throws<InvalidOperationException>(() => player.State(default(MatchStateKey<Tally>)));
    }

    [Fact]
    public void APhaseKeyThrowsBeforeThatPhaseEverOpened()
    {
        LobbyStateKeys keys = new(generation: 2);
        LobbyStateKey<Tally> key = keys.Claim<Tally>();
        Player player = new(1, "one", 0, SERVER_GENERATION);

        Assert.Throws<InvalidOperationException>(() => player.State(key));
    }

    [Fact]
    public void ALobbyKeyThrowsOnceThatLobbyEnded()
    {
        LobbyStateKeys keys = new(generation: 2);
        LobbyStateKey<Tally> key = keys.Claim<Tally>();
        Player player = new(1, "one", 0, SERVER_GENERATION);
        player.OpenLobby(keys.Count, keys.Generation);
        player.State(key).Count = 9;

        player.CloseLobby();

        Assert.Throws<InvalidOperationException>(() => player.State(key));
    }

    [Fact]
    public void AKeyFromTheOldLobbyNeverResolvesAgainstTheNewOne()
    {
        LobbyStateKeys first = new(generation: 2);
        LobbyStateKey<Tally> stale = first.Claim<Tally>();
        Player player = new(1, "one", 0, SERVER_GENERATION);
        player.OpenLobby(first.Count, first.Generation);
        player.State(stale).Count = 9;
        player.CloseLobby();

        // Same index, fresh generation: the old key must not read this cell.
        LobbyStateKeys second = new(generation: 4);
        LobbyStateKey<Tally> live = second.Claim<Tally>();
        player.OpenLobby(second.Count, second.Generation);

        Assert.Throws<InvalidOperationException>(() => player.State(stale));
        Assert.Equal(0, player.State(live).Count);
    }

    [Fact]
    public void ServerStateSurvivesAPhaseChange()
    {
        ServerStateKeys serverKeys = new(SERVER_GENERATION);
        ServerStateKey<Tally> serverKey = serverKeys.Claim<Tally>();
        LobbyStateKeys lobbyKeys = new(generation: 2);
        LobbyStateKey<Tally> lobbyKey = lobbyKeys.Claim<Tally>();
        Player player = NewPlayer(serverKeys);
        player.OpenLobby(lobbyKeys.Count, lobbyKeys.Generation);
        player.State(serverKey).Count = 7;
        player.State(lobbyKey).Count = 7;

        player.CloseLobby();
        MatchStateKeys matchKeys = new(generation: 3);
        MatchStateKey<Tally> matchKey = matchKeys.Claim<Tally>();
        player.OpenMatch(matchKeys.Count, matchKeys.Generation);

        Assert.Equal(7, player.State(serverKey).Count);
        Assert.Equal(0, player.State(matchKey).Count);
        Assert.Throws<InvalidOperationException>(() => player.State(lobbyKey));
    }

    [Fact]
    public void MatchStateDoesNotSurviveTheReturnToTheLobby()
    {
        MatchStateKeys keys = new(generation: 3);
        MatchStateKey<Note> key = keys.Claim<Note>();
        Player player = new(1, "one", 0, SERVER_GENERATION);
        player.OpenMatch(keys.Count, keys.Generation);
        player.State(key).Text = "carried";

        player.CloseMatch();

        Assert.Throws<InvalidOperationException>(() => player.State(key));
    }

    [Fact]
    public void LobbyAndMatchKeysAtTheSameIndexDoNotAlias()
    {
        LobbyStateKeys lobbyKeys = new(generation: 2);
        LobbyStateKey<Tally> lobbyKey = lobbyKeys.Claim<Tally>();
        MatchStateKeys matchKeys = new(generation: 2);
        MatchStateKey<Tally> matchKey = matchKeys.Claim<Tally>();
        Player player = new(1, "one", 0, SERVER_GENERATION);
        player.OpenLobby(lobbyKeys.Count, lobbyKeys.Generation);
        player.OpenMatch(matchKeys.Count, matchKeys.Generation);

        player.State(lobbyKey).Count = 4;

        Assert.Equal(0, player.State(matchKey).Count);
    }

    [Fact]
    public void AKeyFromAHandRolledClaimerThrows()
    {
        ServerStateKeys real = new(SERVER_GENERATION);
        real.Claim<Tally>();
        Player player = NewPlayer(real);
        ServerStateKeys forged = new(generation: 99);
        ServerStateKey<Tally> forgedKey = forged.Claim<Tally>();

        Assert.Throws<InvalidOperationException>(() => player.State(forgedKey));
    }

    [Fact]
    public void ClosingAPhaseTwiceLeavesTheKeyStale()
    {
        LobbyStateKeys keys = new(generation: 2);
        LobbyStateKey<Tally> key = keys.Claim<Tally>();
        Player player = new(1, "one", 0, SERVER_GENERATION);
        player.OpenLobby(keys.Count, keys.Generation);
        player.State(key);

        player.CloseLobby();
        player.CloseLobby();

        Assert.Throws<InvalidOperationException>(() => player.State(key));
    }

    private static Player NewPlayer(ServerStateKeys keys) =>
        new(1, "one", keys.Count, keys.Generation);
}
