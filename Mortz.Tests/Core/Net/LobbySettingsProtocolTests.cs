using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Tests.Net;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips over the loopback NetTransport, same harness as
/// MatchProtocolTests.</summary>
[Collection("NetTransport")]
public class LobbySettingsProtocolTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public void Dispose() => NetTransport.Send = _original;

    private sealed record Outcome(LobbySettings? Settings, LobbySettingsRejectReason? Reason);

    private static Outcome Send(LobbySettingsMsg message)
    {
        NetRouter router = new();
        ClientProbe<LobbySettingsMsg> probe = new();
        router.Add(probe);
        NetTransport.Send = (id, payload, _, _) => Assert.True(router.Dispatch(id, payload));
        message.Broadcast();
        LobbySettingsMsg received = Assert.Single(probe.Messages);
        return LobbySettingsProtocol.TryDecode(received, out LobbySettings? settings,
            out LobbySettingsRejectReason reason)
            ? new Outcome(settings, null)
            : new Outcome(null, reason);
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
        new MatchConfig
        {
            Rules = new ModeRules
            {
                Victory = new KillsVictoryRules { Target = 12 },
            },
        });

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
        Assert.Equal(12,
            Assert.IsType<KillsVictoryRules>(received.Config.Rules.Victory).Target);
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
