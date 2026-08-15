using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Players;
using Mortz.Client.Session;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Net;
using Mortz.Tests.Net;
using Xunit;
using ModeRules = Mortz.Core.Match.Configuration.ModeRules;

namespace Mortz.Tests.Client.Menus;

[Collection(nameof(MortzGodotCollection))]
public class RosterCompositionTests : NodeServiceTest
{
    private const string ROSTER_PATH = "Content/Main/Sidebar/LobbyCard/Margin/Column/Roster";

    [Fact]
    public void TeamsToggleSwapsRosterLayoutsAndDistributesMembers()
    {
        Lobby lobby = MountLobby();
        Control roster = lobby.GetNode<Control>(ROSTER_PATH);
        Assert.IsType<SingleColumnRoster>(roster.GetNode("SingleColumnRoster"));

        Settings(teams: true).Broadcast(Router);
        new LobbyStateMsg(TwoBlueOneRed(), []).Broadcast(Router);

        TeamColumnsRoster columns =
            Assert.IsType<TeamColumnsRoster>(roster.GetNode("TeamColumnsRoster"));
        Assert.Null(roster.GetNodeOrNull("SingleColumnRoster"));

        Node team1 = columns.GetNode("Column/Teams/Team1/Slots");
        Node team2 = columns.GetNode("Column/Teams/Team2/Slots");
        Assert.Equal(2, team1.GetChildCount());
        Assert.Equal(2, team2.GetChildCount());
        Assert.Empty(team1.GetChildren().OfType<Button>());
        Button join = Assert.Single(team2.GetChildren().OfType<Button>());
        Assert.False(join.Disabled);
        Assert.Equal(0, columns.GetNode("Column/Unassigned").GetChildCount());

        Assert.Equal("SWAP", MemberSlotButtons(team2)[0].Text);
        Assert.Empty(MemberSlotButtons(team1));
        new LobbyStateMsg(TwoBlueOneRed(), [new SwapOffer(2, 1)]).Broadcast(Router);
        Assert.Equal("ACCEPT",
            MemberSlotButtons(columns.GetNode("Column/Teams/Team2/Slots"))[0].Text);

        Settings(teams: false).Broadcast(Router);
        Assert.IsType<SingleColumnRoster>(roster.GetNode("SingleColumnRoster"));
        Assert.Null(roster.GetNodeOrNull("TeamColumnsRoster"));
    }

    [Fact]
    public void ValueUpdatesNeverRebuildTheActiveVariant()
    {
        Lobby lobby = MountLobby();
        Settings(teams: true).Broadcast(Router);
        Node before = lobby.GetNode(ROSTER_PATH + "/TeamColumnsRoster");

        Settings(teams: true, killTarget: 42).Broadcast(Router);

        Assert.Equal(before, lobby.GetNode(ROSTER_PATH + "/TeamColumnsRoster"));
    }

    [Fact]
    public void ScoreHudVariantsAreComposed()
    {
        AssertSceneType<PlayerKillsHud>("UI/Hud/PlayerKillsHud");
        AssertSceneType<TeamKillsHud>("UI/Hud/TeamKillsHud");
        AssertSceneType<SingleColumnRoster>("UI/Controls/SingleColumnRoster");
        AssertSceneType<TeamColumnsRoster>("UI/Controls/TeamColumnsRoster");
    }

    private Lobby MountLobby()
    {
        FakeNetwork network = new() { LocalPeerId = 1 };
        ClientAdmin admin = new();
        admin.FakeDependency<INetwork>(network);
        admin.FakeDependency<IClientSender>(Sender);
        admin.FakeDependency(Router);
        ClientPlayers players = HostRouted(new ClientPlayers());
        Pings pings = new();
        pings.FakeDependency(players);
        SessionWins wins = new();
        wins.FakeDependency(players);
        ServiceRoot root = Host(new ServiceRoot
        {
            Setup = HostRouted(new MatchSetup()),
            Pings = HostRouted(pings),
            Wins = HostRouted(wins),
            Players = players,
            Admin = Host(admin),
            Network = network,
            Sender = Sender,
            Router = Router,
            SessionExit = new FakeSessionExit(),
        });
        Lobby lobby = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/UI/Menus/Lobby.tscn").Instantiate<Lobby>();
        root.AddChild(lobby);
        return lobby;
    }

    /// <summary>The layout the roster test seats: 1 and 3 blue, 2 red.</summary>
    private static LobbyMember[] TwoBlueOneRed() =>
    [
        new(1, "A", false, Team.BLUE),
        new(2, "B", false, Team.RED),
        new(3, "C", false, Team.BLUE),
    ];

    private static List<Button> MemberSlotButtons(Node column) =>
        column.GetChildren().Where(child => child is not Button)
            .SelectMany(slot => slot.FindChildren("*", "Button", recursive: true, owned: false)
                .OfType<Button>())
            .ToList();

    private static LobbySettingsMsg Settings(bool teams, int killTarget = 20)
    {
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                Teams = teams,
                Victory = new KillsVictoryRules { Target = killTarget },
            },
        };
        return new LobbySettingsMsg("castlewars", "hash",
            [new ContentOption("castlewars", "Castle Wars")], [], "", config.ToBytes());
    }

    private static void AssertSceneType<T>(string name) where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            $"res://src/Shared/{name}.tscn");
        T node = scene.Instantiate<T>();
        node.Free();
    }
}
