using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Net;
using Xunit;

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

        Settings(teams: true).Broadcast();
        new LobbyStateMsg([1, 2, 3], ["A", "B", "C"], [0, 0, 0], [1, 2, 1], [], [])
            .Broadcast();

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
        new LobbyStateMsg([1, 2, 3], ["A", "B", "C"], [0, 0, 0], [1, 2, 1], [2], [1])
            .Broadcast();
        Assert.Equal("ACCEPT",
            MemberSlotButtons(columns.GetNode("Column/Teams/Team2/Slots"))[0].Text);

        Settings(teams: false).Broadcast();
        Assert.IsType<SingleColumnRoster>(roster.GetNode("SingleColumnRoster"));
        Assert.Null(roster.GetNodeOrNull("TeamColumnsRoster"));
    }

    [Fact]
    public void ValueUpdatesNeverRebuildTheActiveVariant()
    {
        Lobby lobby = MountLobby();
        Settings(teams: true).Broadcast();
        Node before = lobby.GetNode(ROSTER_PATH + "/TeamColumnsRoster");

        Settings(teams: true, killTarget: 42).Broadcast();

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
        MatchSetup setup = Host(new MatchSetup());
        ClientStats stats = Host(new ClientStats());
        ClientAdmin admin = new();
        admin.FakeDependency<INetwork>(network);
        Host(admin);
        Lobby lobby = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/UI/Menus/Lobby.tscn").Instantiate<Lobby>();
        lobby.FakeDependency(setup);
        lobby.FakeDependency(stats);
        lobby.FakeDependency(admin);
        lobby.FakeDependency<INetwork>(network);
        return Host(lobby);
    }

    private static List<Button> MemberSlotButtons(Node column) =>
        column.GetChildren().Where(child => child is not Button)
            .SelectMany(slot => slot.FindChildren("*", "Button", recursive: true, owned: false)
                .OfType<Button>())
            .ToList();

    private static LobbySettingsMsg Settings(bool teams, int killTarget = 20)
    {
        MatchConfig config = new() { Teams = teams, KillTarget = killTarget };
        return new LobbySettingsMsg("castlewars", "hash", ["castlewars"], ["Castle Wars"],
            config.ToBytes());
    }

    private static void AssertSceneType<T>(string name) where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            $"res://src/Shared/{name}.tscn");
        T node = scene.Instantiate<T>();
        node.Free();
    }
}
