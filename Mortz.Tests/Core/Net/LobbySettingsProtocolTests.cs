using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Tests.Net;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Core.Net;

/// <summary>Round trips through the client router.</summary>
public class LobbySettingsProtocolTests
{
    private sealed record Outcome(LobbySettings? Settings, LobbySettingsRejectReason? Reason);

    private static Outcome Send(LobbySettingsMsg message)
    {
        NetRouter router = new();
        ClientProbe<LobbySettingsMsg> probe = new();
        router.Add(probe);
        message.Broadcast(router);
        LobbySettingsMsg received = Assert.Single(probe.Messages);
        return Decode(received);
    }

    private static Outcome Decode(LobbySettingsMsg message)
    {
        return LobbySettingsProtocol.TryDecode(message, out LobbySettings? settings,
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

    private static LobbySettingsMsg Raw(ContentOption[] maps, ContentOption[] modes,
        byte[]? config = null) =>
        new("castlewars", "hash", maps, modes, "",
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
    public void ADefaultMapCatalogEntryIsRejected()
    {
        Outcome outcome = Decode(Raw([default], []));

        Assert.Null(outcome.Settings);
        Assert.Equal(LobbySettingsRejectReason.MAP_CATALOG, outcome.Reason);
    }

    [Fact]
    public void ADefaultModeCatalogEntryIsRejected()
    {
        Outcome outcome = Decode(Raw([], [default]));

        Assert.Null(outcome.Settings);
        Assert.Equal(LobbySettingsRejectReason.MODE_CATALOG, outcome.Reason);
    }

    [Fact]
    public void AnOverCapCatalogIsRejected()
    {
        ContentOption[] rows = Enumerable.Range(0, NetConfig.MAX_LOBBY_MAPS + 1)
            .Select(i => new ContentOption($"map{i}", $"Map {i}")).ToArray();

        Assert.Equal(LobbySettingsRejectReason.MAP_CATALOG, Send(Raw(rows, [])).Reason);
    }

    [Fact]
    public void AContentOptionRejectsBlankValues()
    {
        Assert.Throws<ArgumentException>(() => new ContentOption(" ", "Map"));
        Assert.Throws<ArgumentException>(() => new ContentOption("map", ""));
    }

    [Fact]
    public void UnparseableConfigBytesAreRejected()
    {
        Outcome outcome = Send(Raw([new ContentOption("a", "A")], [], [1, 2, 3]));

        Assert.Null(outcome.Settings);
        Assert.Equal(LobbySettingsRejectReason.CONFIG, outcome.Reason);
    }
}
