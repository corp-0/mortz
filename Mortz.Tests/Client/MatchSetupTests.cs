using Godot;
using Mortz.Client.Setup;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Replication;
using Mortz.Core.Terrain;
using Mortz.Tests.Net;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class MatchSetupTests : NodeServiceTest
{
    private static ModeRules Rules(int target, bool teams = false) => new()
    {
        Teams = teams,
        Victory = new KillsVictoryRules { Target = target },
    };

    private static LobbySettingsMsg Settings(MatchConfig config,
        string mapId = "castlewars", string hash = "hash") =>
        new(mapId, hash, [new ContentOption(mapId, "Castle Wars")],
            [new ContentOption("deathmatch", "Deathmatch")], "", config.ToBytes());

    [Fact]
    public void SettingsApplyAndEventsFireOnTransitionsOnly()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        int config = 0, teams = 0, settings = 0;
        setup.ConfigChanged += () => config++;
        setup.TeamsChanged += () => teams++;
        setup.SettingsChanged += () => settings++;

        Settings(new MatchConfig { Rules = Rules(5, teams: true) }).Broadcast(Router);

        Assert.NotNull(setup.Selection);
        Assert.True(setup.Config.Rules.Teams);
        Assert.Equal(5,
            Assert.IsType<KillsVictoryRules>(setup.Config.Rules.Victory).Target);
        LobbySelection selection = setup.Selection!;
        Assert.Equal("castlewars", selection.MapId);
        Assert.Equal([new ContentOption("castlewars", "Castle Wars")], selection.Maps.Options);
        Assert.Equal((1, 1, 1), (config, teams, settings));

        Settings(new MatchConfig { Rules = Rules(5, teams: true) }).Broadcast(Router);
        Assert.Equal((1, 1, 1), (config, teams, settings));

        Settings(new MatchConfig { Rules = Rules(6, teams: true) }).Broadcast(Router);
        Assert.Equal((2, 1, 2), (config, teams, settings));
    }

    [Fact]
    public void ModeCatalogAppliesAndDerivedModeFlipRaisesSettings()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        MatchConfig config = new();

        new LobbySettingsMsg("castlewars", "hash",
            [new ContentOption("castlewars", "Castle Wars")],
            [new ContentOption("deathmatch", "Deathmatch"),
                new ContentOption("teamdeathmatch", "Team Deathmatch")],
            "deathmatch", config.ToBytes()).Broadcast(Router);
        int settings = 0;
        setup.SettingsChanged += () => settings++;

        LobbySelection selection = setup.Selection!;
        Assert.Equal("deathmatch", selection.ModeId);
        Assert.Equal([
            new ContentOption("deathmatch", "Deathmatch"),
            new ContentOption("teamdeathmatch", "Team Deathmatch"),
        ], selection.Modes.Options);

        // same rules, no longer matching a preset server-side
        new LobbySettingsMsg("castlewars", "hash",
            [new ContentOption("castlewars", "Castle Wars")],
            [new ContentOption("deathmatch", "Deathmatch"),
                new ContentOption("teamdeathmatch", "Team Deathmatch")],
            "", config.ToBytes()).Broadcast(Router);

        Assert.Null(setup.Selection!.ModeId);
        Assert.Equal(1, settings);
    }

    [Fact]
    public void ACatalogOnlyChangeRaisesSettings()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        MatchConfig config = new();
        new LobbySettingsMsg("castlewars", "hash",
            [new ContentOption("castlewars", "Castle Wars")],
            [new ContentOption("deathmatch", "Deathmatch")],
            "deathmatch", config.ToBytes()).Broadcast(Router);
        int settings = 0;
        setup.SettingsChanged += () => settings++;

        // same map, mode and config, one more map on offer
        new LobbySettingsMsg("castlewars", "hash",
            [new ContentOption("castlewars", "Castle Wars"), new ContentOption("arena", "Arena")],
            [new ContentOption("deathmatch", "Deathmatch")], "deathmatch",
            config.ToBytes()).Broadcast(Router);

        Assert.Equal(1, settings);
        Assert.Equal(2, setup.Selection!.Maps.Options.Count);
    }

    [Fact]
    public void CopyConfigGivesEditorsAnIndependentConfig()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        Settings(new MatchConfig { Rules = Rules(9) }).Broadcast(Router);

        MatchConfig copy = setup.CopyConfig();
        Assert.IsType<KillsVictoryRules>(copy.Rules.Victory).Target = 123;
        copy.Physics.Gravity = 123;
        copy.Combat.MortarDamage = 123;

        Assert.Equal(9,
            Assert.IsType<KillsVictoryRules>(setup.Config.Rules.Victory).Target);
        Assert.NotEqual(123, setup.Config.Physics.Gravity);
        Assert.NotEqual(123, setup.Config.Combat.MortarDamage);
    }

    [Fact]
    public void InvalidServerSettingsSurfaceAnErrorAndKeepState()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        Settings(new MatchConfig { Rules = Rules(7) }).Broadcast(Router);
        int settings = 0;
        setup.SettingsChanged += () => settings++;

        setup.Handle(new LobbySettingsMsg("x", "h", [default], [], "",
            new MatchConfig().ToBytes()));

        Assert.Equal("Server sent an invalid map catalog.", setup.SettingsError);
        Assert.Equal(7,
            Assert.IsType<KillsVictoryRules>(setup.Config.Rules.Victory).Target);
        Assert.NotNull(setup.Selection);
        Assert.Equal(1, settings);

        new LobbySettingsMsg("x", "h", [new ContentOption("a", "A")], [], "", [1, 2, 3])
            .Broadcast(Router);
        Assert.Equal("Server sent invalid match settings.", setup.SettingsError);
        Assert.Equal(2, settings);

        Settings(new MatchConfig { Rules = Rules(7) }).Broadcast(Router);
        Assert.Null(setup.SettingsError);
        Assert.Equal(3, settings);
    }

    [Fact]
    public void SwapOffersRideTheLobbyStateAndFireOnTransitionsOnly()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        int offers = 0;
        setup.SwapOffersChanged += () => offers++;

        LobbyMember[] members =
        [
            new LobbyMember(1, "A", false, Team.BLUE),
            new LobbyMember(2, "B", false, Team.RED),
        ];
        new LobbyStateMsg(members, [new SwapOffer(1, 2)]).Broadcast(Router);
        Assert.Equal([new SwapOffer(1, 2)], setup.SwapOffers);
        Assert.Equal(1, offers);

        new LobbyStateMsg(members, [new SwapOffer(1, 2)]).Broadcast(Router);
        Assert.Equal(1, offers);

        new LobbyStateMsg(members, []).Broadcast(Router);
        Assert.Empty(setup.SwapOffers);
        Assert.Equal(2, offers);
    }

    [Fact]
    public void MatchLoadCarriesTheFrozenRulesButNoCatalog()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        int teams = 0;
        setup.TeamsChanged += () => teams++;

        new MatchLoadMsg("arena", "abc", new MatchConfig { Rules = new ModeRules { Teams = true } }.ToBytes(),
            (byte)TerrainSyncEncoding.CARVE_LOG, 1, 10, 1,
            MatchSeat.PLAYER, MatchActivity.ACTIVE, SpectateReason.NONE, -1,
            new MatchSnapshot(0, []).SerializeFor(1), -1).SendTo(Router, 1);

        Assert.Null(setup.Selection);
        Assert.True(setup.Config.Rules.Teams);
        Assert.Equal(1, teams);
    }

    [Fact]
    public void NodeOutsideTheTreeIgnoresTraffic()
    {
        MatchSetup setup = HostRouted(new MatchSetup());
        setup.GetParent<Node>().RemoveChild(setup);

        Settings(new MatchConfig()).Broadcast(Router);

        Assert.Null(setup.Selection);
    }
}
