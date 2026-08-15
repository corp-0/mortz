using Mortz.Server.Phases;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>Payload disposal at lifetime end: who, when, and in what order.
/// Payloads need a parameterless constructor, so they report through a log the
/// test owns and clears.</summary>
public class PlayerDisposalTests
{
    private static readonly List<string> _log = [];

    private abstract class Payload : IDisposable
    {
        public void Dispose() => _log.Add(GetType().Name);
    }

    private sealed class First : Payload;

    private sealed class Second : Payload;

    private sealed class Third : Payload;

    private sealed class Quiet
    {
        public int Count;
    }

    public PlayerDisposalTests() => _log.Clear();

    [Fact]
    public void ServerPayloadsAreDisposedInReverseClaimOrder()
    {
        ServerStateKeys keys = new(generation: 1);
        ServerStateKey<First> first = keys.Claim<First>();
        ServerStateKey<Second> second = keys.Claim<Second>();
        ServerStateKey<Third> third = keys.Claim<Third>();
        Player player = new(1, "one", keys.Count, keys.Generation);
        player.State(first);
        player.State(second);
        player.State(third);

        player.Close(ServerPhaseKind.LOBBY);

        Assert.Equal(["Third", "Second", "First"], _log);
    }

    [Fact]
    public void PhasePayloadsAreDisposedInReverseClaimOrder()
    {
        MatchStateKeys keys = new(generation: 3);
        MatchStateKey<First> first = keys.Claim<First>();
        MatchStateKey<Second> second = keys.Claim<Second>();
        Player player = new(1, "one", 0, 1);
        player.OpenMatch(keys.Count, keys.Generation);
        player.State(first);
        player.State(second);

        player.CloseMatch();

        Assert.Equal(["Second", "First"], _log);
    }

    [Fact]
    public void EndingAPhaseLeavesServerPayloadsAlone()
    {
        ServerStateKeys serverKeys = new(generation: 1);
        ServerStateKey<First> serverKey = serverKeys.Claim<First>();
        MatchStateKeys matchKeys = new(generation: 2);
        MatchStateKey<Second> matchKey = matchKeys.Claim<Second>();
        Player player = new(1, "one", serverKeys.Count, serverKeys.Generation);
        player.OpenMatch(matchKeys.Count, matchKeys.Generation);
        player.State(serverKey);
        player.State(matchKey);

        player.CloseMatch();

        Assert.Equal(["Second"], _log);
    }

    [Fact]
    public void ClosingDisposesThePhaseCellsBeforeTheServerCells()
    {
        ServerStateKeys serverKeys = new(generation: 1);
        ServerStateKey<First> serverKey = serverKeys.Claim<First>();
        MatchStateKeys matchKeys = new(generation: 3);
        MatchStateKey<Second> matchKey = matchKeys.Claim<Second>();
        Player player = new(1, "one", serverKeys.Count, serverKeys.Generation);
        player.OpenMatch(matchKeys.Count, matchKeys.Generation);
        player.State(serverKey);
        player.State(matchKey);

        player.Close(ServerPhaseKind.MATCH);

        Assert.Equal(["Second", "First"], _log);
    }

    [Fact]
    public void ACellNobodyTouchedIsNeverDisposed()
    {
        ServerStateKeys keys = new(generation: 1);
        ServerStateKey<First> read = keys.Claim<First>();
        keys.Claim<Second>();
        Player player = new(1, "one", keys.Count, keys.Generation);
        player.State(read);

        player.Close(ServerPhaseKind.LOBBY);

        Assert.Equal(["First"], _log);
    }

    [Fact]
    public void APayloadIsDisposedOnceEvenIfThePhaseClosesTwice()
    {
        MatchStateKeys keys = new(generation: 2);
        MatchStateKey<First> key = keys.Claim<First>();
        Player player = new(1, "one", 0, 1);
        player.OpenMatch(keys.Count, keys.Generation);
        player.State(key);

        player.CloseMatch();
        player.CloseMatch();

        Assert.Equal(["First"], _log);
    }

    /// <summary>Phase cells are dropped on close, server cells are not, so Close
    /// is safe to repeat for a phase and not for the connection. This is what
    /// GameServer.Dispose's idempotence flag is protecting.</summary>
    [Fact]
    public void ServerCellsSurviveCloseAndAreDisposedAgainIfItIsRepeated()
    {
        ServerStateKeys keys = new(generation: 1);
        ServerStateKey<First> key = keys.Claim<First>();
        Player player = new(1, "one", keys.Count, keys.Generation);
        player.State(key);

        player.Close(ServerPhaseKind.LOBBY);
        player.Close(ServerPhaseKind.LOBBY);

        Assert.Equal(["First", "First"], _log);
    }

    [Fact]
    public void APayloadThatIsNotDisposableIsSimplyDropped()
    {
        ServerStateKeys keys = new(generation: 1);
        ServerStateKey<Quiet> quiet = keys.Claim<Quiet>();
        ServerStateKey<First> disposable = keys.Claim<First>();
        Player player = new(1, "one", keys.Count, keys.Generation);
        player.State(quiet).Count = 1;
        player.State(disposable);

        player.Close(ServerPhaseKind.LOBBY);

        Assert.Equal(["First"], _log);
    }

    [Fact]
    public void CloseTouchesOnlyThePhaseItWasToldIsActive()
    {
        MatchStateKeys matchKeys = new(generation: 3);
        MatchStateKey<Second> matchKey = matchKeys.Claim<Second>();
        ServerStateKeys serverKeys = new(generation: 1);
        ServerStateKey<First> serverKey = serverKeys.Claim<First>();
        Player player = new(1, "one", serverKeys.Count, serverKeys.Generation);
        player.OpenMatch(matchKeys.Count, matchKeys.Generation);
        player.State(matchKey);
        player.State(serverKey);

        // The lobby is what Close is told is active, so the match cells stay put.
        player.Close(ServerPhaseKind.LOBBY);

        Assert.Equal(["First"], _log);
    }
}
