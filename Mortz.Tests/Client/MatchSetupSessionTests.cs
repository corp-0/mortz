using Mortz.Client.Setup;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Terrain;
using Xunit;

namespace Mortz.Tests.Client;

/// <summary>Drives the setup state through the real wire path (loopback
/// NetTransport), so serialization and dispatch are covered too. Swaps the
/// NetTransport.Send static, hence the shared NetTransport collection.</summary>
[Collection("NetTransport")]
public class MatchSetupSessionTests : IDisposable
{
    private readonly NetTransport.SendDelegate _original = NetTransport.Send;

    public MatchSetupSessionTests() =>
        NetTransport.Send = (id, payload, _, _) =>
            Assert.True(NetRegistry.Dispatch(id, 1, payload, isServer: false));

    public void Dispose() => NetTransport.Send = _original;

    private static LobbySettingsMsg Settings(MatchConfig config,
        string mapId = "castlewars", string hash = "hash") =>
        new(mapId, hash, [mapId], ["Castle Wars"], config.ToBytes());

    [Fact]
    public void SettingsApplyAndEventsFireOnTransitionsOnly()
    {
        using MatchSetupSession session = new();
        session.Subscribe();
        int rules = 0, teams = 0, settings = 0;
        session.RulesChanged += () => rules++;
        session.TeamsChanged += () => teams++;
        session.SettingsChanged += () => settings++;

        Settings(new MatchConfig { Teams = true, KillTarget = 5 }).Broadcast();

        Assert.True(session.HasServerState);
        Assert.True(session.Rules.Teams);
        Assert.Equal(5, session.Rules.KillTarget);
        Assert.Equal("castlewars", session.MapId);
        Assert.Equal([new MapOption("castlewars", "Castle Wars")], session.MapOptions);
        Assert.Equal((1, 1, 1), (rules, teams, settings));

        Settings(new MatchConfig { Teams = true, KillTarget = 5 }).Broadcast();
        Assert.Equal((1, 1, 1), (rules, teams, settings));

        Settings(new MatchConfig { Teams = true, KillTarget = 6 }).Broadcast();
        Assert.Equal((2, 1, 2), (rules, teams, settings));
    }

    [Fact]
    public void CopyRulesGivesEditorsAnIndependentConfig()
    {
        using MatchSetupSession session = new();
        session.Subscribe();
        Settings(new MatchConfig { KillTarget = 9 }).Broadcast();

        MatchConfig copy = session.CopyRules();
        copy.KillTarget = 123;

        Assert.Equal(9, session.Rules.KillTarget);
    }

    [Fact]
    public void InvalidServerSettingsSurfaceAnErrorAndKeepState()
    {
        using MatchSetupSession session = new();
        session.Subscribe();
        Settings(new MatchConfig { KillTarget = 7 }).Broadcast();
        int settings = 0;
        session.SettingsChanged += () => settings++;

        new LobbySettingsMsg("x", "h", ["a"], ["A", "B"], new MatchConfig().ToBytes())
            .Broadcast();

        Assert.Equal("Server sent an invalid map catalog.", session.SettingsError);
        Assert.Equal(7, session.Rules.KillTarget);
        Assert.True(session.HasServerState);
        Assert.Equal(1, settings);

        new LobbySettingsMsg("x", "h", ["a"], ["A"], [1, 2, 3]).Broadcast();
        Assert.Equal("Server sent invalid match settings.", session.SettingsError);
        Assert.Equal(2, settings);

        Settings(new MatchConfig { KillTarget = 7 }).Broadcast();
        Assert.Equal("", session.SettingsError);
        Assert.Equal(3, settings);
    }

    [Fact]
    public void LobbyStateSplitsRosterAndTeamEvents()
    {
        using MatchSetupSession session = new();
        session.Subscribe();
        int roster = 0, teams = 0;
        session.RosterChanged += () => roster++;
        session.TeamsChanged += () => teams++;

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 0], [0, 0]).Broadcast();
        Assert.Equal((1, 0), (roster, teams));
        Assert.Equal([
            new LobbyMember(1, "A", true, 0),
            new LobbyMember(2, "B", false, 0),
        ], session.Members);

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 0], [0, 0]).Broadcast();
        Assert.Equal((1, 0), (roster, teams));

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 0], [1, 2]).Broadcast();
        Assert.Equal((1, 1), (roster, teams));

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 1], [1, 2]).Broadcast();
        Assert.Equal((2, 1), (roster, teams));
    }

    [Fact]
    public void WelcomeCarriesTheFrozenRulesForLateJoiners()
    {
        using MatchSetupSession session = new();
        session.Subscribe();
        int teams = 0;
        session.TeamsChanged += () => teams++;

        new WelcomeMsg("arena", "abc", new MatchConfig { Teams = true }.ToBytes(),
            (byte)TerrainSyncEncoding.CARVE_LOG, 1, 10, 1).SendTo(1);

        Assert.True(session.HasServerState);
        Assert.True(session.Rules.Teams);
        Assert.Equal("arena", session.MapId);
        Assert.Equal(1, teams);
    }

    [Fact]
    public void ClearForgetsEverythingAndNotifies()
    {
        using MatchSetupSession session = new();
        session.Subscribe();
        Settings(new MatchConfig { Teams = true }).Broadcast();
        new LobbyStateMsg([1], ["A"], [0], [1]).Broadcast();
        int teams = 0, roster = 0, settings = 0;
        session.TeamsChanged += () => teams++;
        session.RosterChanged += () => roster++;
        session.SettingsChanged += () => settings++;

        session.Clear();

        Assert.False(session.HasServerState);
        Assert.False(session.Rules.Teams);
        Assert.Empty(session.Members);
        Assert.Equal("", session.MapId);
        Assert.Empty(session.MapOptions);
        // Teams fires twice: the rule toggled off AND the assignments cleared.
        Assert.Equal((2, 1, 1), (teams, roster, settings));
    }

    [Fact]
    public void UnsubscribedSessionIgnoresTraffic()
    {
        using MatchSetupSession subscribed = new();
        subscribed.Subscribe();
        using MatchSetupSession unsubscribed = new();

        Settings(new MatchConfig()).Broadcast();

        Assert.True(subscribed.HasServerState);
        Assert.False(unsubscribed.HasServerState);
    }
}
