using Godot;
using Mortz.Client;
using Mortz.Client.Match;
using Mortz.Client.Menus;
using Mortz.Client.Setup;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client.Menus;

/// <summary>The lobby roster host swaps whole layouts on the Teams rule and
/// the variants distribute the replicated members. Drives the real ClientMain
/// composition through MatchSetup's test hooks.</summary>
[Collection(nameof(GodotHeadlessCollection))]
public class RosterCompositionTests
{
    private const string ROSTER_PATH = "Lobby/Content/Main/Sidebar/LobbyCard/Margin/Column/Roster";

    [Fact]
    public void TeamsToggleSwapsRosterLayoutsAndDistributesMembers()
    {
        SceneTree tree = Assert.IsType<SceneTree>(Engine.GetMainLoop());
        PackedScene clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        tree.Root.AddChild(client);
        try
        {
            MatchSetup setup = client.GetNode<MatchSetup>("MatchSetup");
            Control roster = client.GetNode<Control>(ROSTER_PATH);
            Assert.IsType<SingleColumnRoster>(roster.GetNode("SingleColumnRoster"));

            setup.ApplySettingsForTest(Settings(teams: true));
            setup.ApplyLobbyStateForTest(new LobbyStateMsg(
                [1, 2, 3], ["A", "B", "C"], [0, 0, 0], [1, 2, 1]));

            TeamColumnsRoster columns =
                Assert.IsType<TeamColumnsRoster>(roster.GetNode("TeamColumnsRoster"));
            Assert.Null(roster.GetNodeOrNull("SingleColumnRoster"));
            Assert.Equal(2, columns.GetNode("Column/Teams/Team1/Slots").GetChildCount());
            Assert.Equal(1, columns.GetNode("Column/Teams/Team2/Slots").GetChildCount());
            Assert.Equal(0, columns.GetNode("Column/Unassigned").GetChildCount());

            setup.ApplySettingsForTest(Settings(teams: false));
            Assert.IsType<SingleColumnRoster>(roster.GetNode("SingleColumnRoster"));
            Assert.Null(roster.GetNodeOrNull("TeamColumnsRoster"));
        }
        finally
        {
            tree.Root.RemoveChild(client);
            client.Free();
        }
    }

    [Fact]
    public void ValueUpdatesNeverRebuildTheActiveVariant()
    {
        SceneTree tree = Assert.IsType<SceneTree>(Engine.GetMainLoop());
        PackedScene clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        ClientMain client = clientScene.Instantiate<ClientMain>();
        tree.Root.AddChild(client);
        try
        {
            MatchSetup setup = client.GetNode<MatchSetup>("MatchSetup");
            setup.ApplySettingsForTest(Settings(teams: true));
            Node before = client.GetNode(ROSTER_PATH + "/TeamColumnsRoster");

            setup.ApplySettingsForTest(Settings(teams: true, killTarget: 42));

            Assert.Equal(before, client.GetNode(ROSTER_PATH + "/TeamColumnsRoster"));
        }
        finally
        {
            tree.Root.RemoveChild(client);
            client.Free();
        }
    }

    [Fact]
    public void ScoreHudVariantsAreComposed()
    {
        AssertSceneType<PlayerKillsHud>("PlayerKillsHud");
        AssertSceneType<TeamKillsHud>("TeamKillsHud");
        AssertSceneType<SingleColumnRoster>("SingleColumnRoster");
        AssertSceneType<TeamColumnsRoster>("TeamColumnsRoster");
    }

    private static LobbySettingsMsg Settings(bool teams, int killTarget = 20)
    {
        MatchConfig config = new() { Teams = teams, KillTarget = killTarget };
        return new LobbySettingsMsg("castlewars", "hash", ["castlewars"], ["Castle Wars"],
            config.ToBytes());
    }

    private static void AssertSceneType<T>(string name) where T : Node
    {
        PackedScene scene = ResourceLoader.Load<PackedScene>(
            $"res://src/Shared/Scenes/{name}.tscn");
        T node = scene.Instantiate<T>();
        node.Free();
    }
}
