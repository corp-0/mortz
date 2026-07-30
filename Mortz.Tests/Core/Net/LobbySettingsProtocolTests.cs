using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// MatchProtocolTests.</summary>
[Collection("NetTransport")]
public class LobbySettingsProtocolTests : IDisposable
{
    private const long SENDER = 42;

    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private static void UseLoopback() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, SENDER, payload, isServer: false));

    private sealed record Outcome(LobbySettings? Settings, LobbySettingsRejectReason? Reason);

    private static Outcome Send(LobbySettingsMsg message)
    {
        UseLoopback();
        LobbySettings? settings = null;
        LobbySettingsRejectReason? reason = null;
        Action<LobbySettings> onReceived = decoded => settings = decoded;
        Action<LobbySettingsRejectReason> onRejected = rejected => reason = rejected;
        LobbySettingsProtocol.Received += onReceived;
        LobbySettingsProtocol.Rejected += onRejected;
        try
        {
            message.Broadcast();
        }
        finally
        {
            LobbySettingsProtocol.Received -= onReceived;
            LobbySettingsProtocol.Rejected -= onRejected;
        }
        return new Outcome(settings, reason);
    }

    private static Outcome Send(LobbySettings settings) =>
        Send(LobbySettingsProtocol.Encode(settings));

    private static LobbySettings Valid(string? modeId) => new(
        new LobbySelection(
            "castlewars",
            "hash",
            new LobbyCatalog([new ContentOption("castlewars", "Castle Wars")]),
            new LobbyCatalog([new ContentOption("deathmatch", "Deathmatch")]),
            modeId),
        new MatchConfig { Rules = new ModeRules { KillTarget = 12 } });

    private static LobbySettingsMsg Raw(string[] mapIds, string[] mapNames,
        string[] modeIds, string[] modeNames, byte[]? config = null) =>
        new("castlewars", "hash", mapIds, mapNames, modeIds, modeNames, "",
            config ?? new MatchConfig().ToBytes());

    [Fact]
    public void SettingsRoundTrip()
    {
        LobbySettings sent = Valid("deathmatch");
        LobbySettings? received = Send(sent).Settings;

        Assert.NotNull(received);
        Assert.Equal(sent.Selection, received.Selection);
        Assert.Equal("deathmatch", received.Selection.ModeId);
        Assert.Equal(12, received.Config.Rules.KillTarget);
    }

    [Fact]
    public void NoMatchingModeArrivesAsNoMode() =>
        Assert.Null(Send(Valid(null)).Settings!.Selection.ModeId);

    [Fact]
    public void AMismatchedMapCatalogIsRejected()
    {
        Outcome outcome = Send(Raw(["a"], ["A", "B"], [], []));

        Assert.Null(outcome.Settings);
        Assert.Equal(LobbySettingsRejectReason.MAP_CATALOG, outcome.Reason);
    }

    [Fact]
    public void AMismatchedModeCatalogIsRejected()
    {
        Outcome outcome = Send(Raw(["a"], ["A"], ["m"], []));

        Assert.Null(outcome.Settings);
        Assert.Equal(LobbySettingsRejectReason.MODE_CATALOG, outcome.Reason);
    }

    [Fact]
    public void AnOverCapCatalogIsRejected()
    {
        string[] ids = Enumerable.Range(0, NetConfig.MAX_LOBBY_MAPS + 1)
            .Select(i => $"map{i}").ToArray();

        Assert.Equal(LobbySettingsRejectReason.MAP_CATALOG, Send(Raw(ids, ids, [], [])).Reason);
    }

    [Fact]
    public void ABlankCatalogEntryIsRejected()
    {
        Assert.Equal(LobbySettingsRejectReason.MAP_CATALOG,
            Send(Raw([" "], ["A"], [], [])).Reason);
        Assert.Equal(LobbySettingsRejectReason.MODE_CATALOG,
            Send(Raw(["a"], ["A"], ["m"], [""])).Reason);
    }

    [Fact]
    public void UnparseableConfigBytesAreRejected()
    {
        Outcome outcome = Send(Raw(["a"], ["A"], [], [], [1, 2, 3]));

        Assert.Null(outcome.Settings);
        Assert.Equal(LobbySettingsRejectReason.CONFIG, outcome.Reason);
    }
}
