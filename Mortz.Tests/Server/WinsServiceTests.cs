using Mortz.Core.Net.Stats;
using Mortz.Server;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Mortz.Server.Wins;
using Serilog.Core;
using Xunit;

namespace Mortz.Tests.Server;

/// <summary>Session win tallies, driven through the framework's state keys.</summary>
public class WinsServiceTests
{
    private readonly RecordingTransport _transport = new();
    private readonly IServerLink _link;
    private readonly ServerStateKeys _keys = new(generation: 1);
    private readonly Roster _roster;
    private readonly WinsService _wins;

    public WinsServiceTests()
    {
        _link = new ReadyLink(_transport);
        _roster = new Roster(_keys);
        _wins = new WinsService(_keys, _roster, _link, Logger.None);
    }

    [Fact]
    public void WinsAccumulatePerPeer()
    {
        Player seven = _roster.Join(7, "seven");
        _roster.Join(8, "eight");
        Player nine = _roster.Join(9, "nine");

        _wins.Record([seven]);
        _wins.Record([seven]);
        _wins.Record([nine]);

        Dictionary<int, int> table = LatestTable();
        Assert.Equal(2, table[7]);
        Assert.Equal(1, table[9]);
        Assert.Equal(0, table[8]);
    }

    [Fact]
    public void LeavingDeletesTheTally()
    {
        Player seven = _roster.Join(7, "seven");
        _wins.Record([seven]);
        Assert.Equal(1, LatestTable()[7]);

        _roster.Leave(7);
        seven.Close(ServerPhaseKind.LOBBY);
        _wins.PlayerJoined(_roster.Join(7, "seven"));

        Assert.Equal(0, LatestTable()[7]);
    }

    private Dictionary<int, int> LatestTable()
    {
        SessionWinsMsg message = _transport.Messages
            .Select(sent => sent.Message)
            .OfType<SessionWinsMsg>()
            .Last();
        Dictionary<int, int> table = new();
        for (int i = 0; i < message.PeerIds.Length; i++)
        {
            table[message.PeerIds[i]] = message.Wins[i];
        }
        return table;
    }
}
