using Godot;
using Mortz.Client.Setup;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Terrain;
using Mortz.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class MatchSetupTests : NodeServiceTest
{
    private static LobbySettingsMsg Settings(MatchConfig config,
        string mapId = "castlewars", string hash = "hash") =>
        new(mapId, hash, [mapId], ["Castle Wars"], config.ToBytes());

    [Fact]
    public void SettingsApplyAndEventsFireOnTransitionsOnly()
    {
        MatchSetup setup = Host(new MatchSetup());
        int rules = 0, teams = 0, settings = 0;
        setup.RulesChanged += () => rules++;
        setup.TeamsChanged += () => teams++;
        setup.SettingsChanged += () => settings++;

        Settings(new MatchConfig { Teams = true, KillTarget = 5 }).Broadcast();

        Assert.True(setup.HasServerState);
        Assert.True(setup.Rules.Teams);
        Assert.Equal(5, setup.Rules.KillTarget);
        Assert.Equal("castlewars", setup.MapId);
        Assert.Equal([new MapOption("castlewars", "Castle Wars")], setup.MapOptions);
        Assert.Equal((1, 1, 1), (rules, teams, settings));

        Settings(new MatchConfig { Teams = true, KillTarget = 5 }).Broadcast();
        Assert.Equal((1, 1, 1), (rules, teams, settings));

        Settings(new MatchConfig { Teams = true, KillTarget = 6 }).Broadcast();
        Assert.Equal((2, 1, 2), (rules, teams, settings));
    }

    [Fact]
    public void CopyRulesGivesEditorsAnIndependentConfig()
    {
        MatchSetup setup = Host(new MatchSetup());
        Settings(new MatchConfig { KillTarget = 9 }).Broadcast();

        MatchConfig copy = setup.CopyRules();
        copy.KillTarget = 123;

        Assert.Equal(9, setup.Rules.KillTarget);
    }

    [Fact]
    public void InvalidServerSettingsSurfaceAnErrorAndKeepState()
    {
        MatchSetup setup = Host(new MatchSetup());
        Settings(new MatchConfig { KillTarget = 7 }).Broadcast();
        int settings = 0;
        setup.SettingsChanged += () => settings++;

        new LobbySettingsMsg("x", "h", ["a"], ["A", "B"], new MatchConfig().ToBytes())
            .Broadcast();

        Assert.Equal("Server sent an invalid map catalog.", setup.SettingsError);
        Assert.Equal(7, setup.Rules.KillTarget);
        Assert.True(setup.HasServerState);
        Assert.Equal(1, settings);

        new LobbySettingsMsg("x", "h", ["a"], ["A"], [1, 2, 3]).Broadcast();
        Assert.Equal("Server sent invalid match settings.", setup.SettingsError);
        Assert.Equal(2, settings);

        Settings(new MatchConfig { KillTarget = 7 }).Broadcast();
        Assert.Equal("", setup.SettingsError);
        Assert.Equal(3, settings);
    }

    [Fact]
    public void LobbyStateSplitsRosterAndTeamEvents()
    {
        MatchSetup setup = Host(new MatchSetup());
        int roster = 0, teams = 0;
        setup.RosterChanged += () => roster++;
        setup.TeamsChanged += () => teams++;

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 0], [0, 0], [], []).Broadcast();
        Assert.Equal((1, 0), (roster, teams));
        Assert.Equal([
            new LobbyMember(1, "A", true, 0),
            new LobbyMember(2, "B", false, 0),
        ], setup.Members);

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 0], [0, 0], [], []).Broadcast();
        Assert.Equal((1, 0), (roster, teams));

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 0], [1, 2], [], []).Broadcast();
        Assert.Equal((1, 1), (roster, teams));

        new LobbyStateMsg([1, 2], ["A", "B"], [1, 1], [1, 2], [], []).Broadcast();
        Assert.Equal((2, 1), (roster, teams));
    }

    [Fact]
    public void SwapOffersRideTheLobbyStateAndFireOnTransitionsOnly()
    {
        MatchSetup setup = Host(new MatchSetup());
        int offers = 0;
        setup.SwapOffersChanged += () => offers++;

        new LobbyStateMsg([1, 2], ["A", "B"], [0, 0], [1, 2], [1], [2]).Broadcast();
        Assert.Equal([new SwapOffer(1, 2)], setup.SwapOffers);
        Assert.Equal(1, offers);

        new LobbyStateMsg([1, 2], ["A", "B"], [0, 0], [1, 2], [1], [2]).Broadcast();
        Assert.Equal(1, offers);

        new LobbyStateMsg([1, 2], ["A", "B"], [0, 0], [2, 1], [], []).Broadcast();
        Assert.Empty(setup.SwapOffers);
        Assert.Equal(2, offers);
    }

    [Fact]
    public void WelcomeCarriesTheFrozenRulesForLateJoiners()
    {
        MatchSetup setup = Host(new MatchSetup());
        int teams = 0;
        setup.TeamsChanged += () => teams++;

        new WelcomeMsg("arena", "abc", new MatchConfig { Teams = true }.ToBytes(),
            (byte)TerrainSyncEncoding.CARVE_LOG, 1, 10, 1).SendTo(1);

        Assert.True(setup.HasServerState);
        Assert.True(setup.Rules.Teams);
        Assert.Equal("arena", setup.MapId);
        Assert.Equal(1, teams);
    }

    [Fact]
    public void NodeOutsideTheTreeIgnoresTraffic()
    {
        MatchSetup setup = Host(new MatchSetup());
        setup.GetParent<Node>().RemoveChild(setup);

        Settings(new MatchConfig()).Broadcast();

        Assert.False(setup.HasServerState);
    }
}
