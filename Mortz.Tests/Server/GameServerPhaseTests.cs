using Mortz.Core.Match;
using Mortz.Core.Match.Participation;
using Mortz.Core.Net.Chat;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Core.Net.Sim;
using Mortz.Server.Phases;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>Phase transitions: what starts one, and what everyone is told when
/// it happens. A transition is a join into the new phase for everybody.</summary>
public class GameServerPhaseTests : IDisposable
{
    private readonly TestServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public void EveryoneReadyStartsTheMatchOnTheNextAdvance()
    {
        Seat(7, 8);

        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);

        _server.Tick();

        Assert.Equal(ServerPhaseKind.MATCH, _server.Server.Phase);
    }

    [Fact]
    public void AnEmptyLobbyNeverStartsAMatch()
    {
        _server.Tick();

        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);
    }

    [Fact]
    public void ReadyThenUnreadyInTheSameTickStartsNothing()
    {
        Seat(7, 8);
        _server.Receive(8, new SetReadyMsg(false));

        _server.Tick();

        Assert.Equal(ServerPhaseKind.LOBBY, _server.Server.Phase);
    }

    [Fact]
    public void ThePhaseOpenRosterPrecedesEveryPlayersCatchUp()
    {
        Seat(7, 8);
        _server.Link.Messages.Clear();

        _server.Tick();

        string[] trace = _server.Link.Trace();
        int roster = Array.IndexOf(trace, "7:RosterMsg");
        int first = Array.IndexOf(trace, "7:WelcomeMsg");
        int second = Array.IndexOf(trace, "8:WelcomeMsg");
        Assert.True(first == 0 && second > first && roster > second,
            $"bootstrap must precede the queued phase roster, got {string.Join(", ", trace)}");
    }

    [Fact]
    public void EveryoneAlreadyConnectedIsCaughtUpOnTheTransition()
    {
        Seat(7, 8);
        _server.Link.Messages.Clear();

        _server.Tick();

        // Not only the joiner: both seated players get the new phase's state.
        Assert.Contains("7:WelcomeMsg", _server.Link.Trace());
        Assert.Contains("8:WelcomeMsg", _server.Link.Trace());
        Assert.Contains("7:ScoreSyncMsg", _server.Link.Trace());
        Assert.Contains("8:ScoreSyncMsg", _server.Link.Trace());
    }

    [Fact]
    public void AMidMatchJoinerIsSeatedAndCaughtUp()
    {
        Seat(7, 8);
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Connect(9, "carol");

        string[] trace = _server.Link.Trace();
        Assert.Contains("9:RosterMsg", trace);
        Assert.Contains("9:WelcomeMsg", trace);
        Assert.Contains("9:LobbySettingsMsg", trace);
        Assert.Contains(9, _server.Link.Last<RosterMsg>().Entries.Select(entry => entry.PeerId));
        Assert.Equal(MatchSeat.PLAYER, _server.Link.Last<WelcomeMsg>().Seat);
    }

    [Fact]
    public void AMidMatchJoinerReceivesEveryPlayersModifiersAfterTheTerrainSync()
    {
        Seat(7, 8);
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Connect(9, "carol");

        // The broadcast copy left before the welcome, when the joiner had no
        // match screen to hear it; the unicast after the terrain is theirs.
        string[] trace = _server.Link.Trace();
        int lastChunk = Array.LastIndexOf(trace, "9:TerrainChunkMsg");
        int[] modifiers = [.. Enumerable.Range(0, trace.Length)
            .Where(index => trace[index] == "9:PlayerModifiersMsg")];
        Assert.Equal(3, modifiers.Length);
        Assert.True(lastChunk >= 0 && modifiers.All(index => index > lastChunk),
            $"modifiers must follow the terrain sync, got {string.Join(", ", trace)}");
    }

    [Fact]
    public void ADisabledJipJoinerIsCaughtUpWithoutEnteringTheSimulationRoster()
    {
        using TestServer server = new(allowJoinInProgress: false);
        Seat(server, 7, 8);
        server.Tick();
        server.Link.Messages.Clear();

        server.Connect(9, "carol");

        Assert.Equal([7, 8], server.Link.Last<RosterMsg>().Entries.Select(entry => entry.PeerId));
        WelcomeMsg welcome = server.Link.Last<WelcomeMsg>();
        Assert.Equal(MatchSeat.SPECTATOR, welcome.Seat);
        Assert.Equal(MatchActivity.SPECTATING, welcome.Activity);
        Assert.Equal(SpectateReason.JIP, welcome.SpectateReason);
        Assert.NotEmpty(welcome.InitialSnapshot);
    }

    [Fact]
    public void LobbyOnlyMessagesAreDroppedDuringAMatch()
    {
        Seat(7, 8);
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Receive(7, new SetReadyMsg(false));

        Assert.Empty(_server.Link.Messages);
        Assert.Equal(ServerPhaseKind.MATCH, _server.Server.Phase);
    }

    [Fact]
    public void MatchOnlyInputsAreIgnoredInTheLobby()
    {
        _server.Connect(7, "alice");
        _server.Link.Messages.Clear();

        _server.Server.Inputs(7, [1, 2, 3]);

        Assert.Empty(_server.Link.Messages);
    }

    [Fact]
    public void ChatKeepsWorkingAcrossThePhaseChange()
    {
        Seat(7, 8);
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Receive(7, new ChatSendMsg("still here"));

        Assert.Equal("still here", _server.Link.Last<ChatMsg>().Text);
    }

    [Fact]
    public void AMatchLeaverIsDroppedFromTheMatchRoster()
    {
        Seat(7, 8);
        _server.Tick();
        _server.Link.Messages.Clear();

        _server.Server.Disconnect(7);

        Assert.Equal([8], _server.Link.Last<RosterMsg>().Entries.Select(entry => entry.PeerId));
    }

    /// <summary>Two players, both ready. The match has not started yet: that
    /// needs an Advance.</summary>
    private void Seat(params int[] peerIds)
    {
        Seat(_server, peerIds);
    }

    private static void Seat(TestServer server, params int[] peerIds)
    {
        foreach (int peerId in peerIds)
        {
            server.Connect(peerId, $"player{peerId}");
        }
        foreach (int peerId in peerIds)
        {
            server.Receive(peerId, new SetReadyMsg(true));
        }
    }
}
