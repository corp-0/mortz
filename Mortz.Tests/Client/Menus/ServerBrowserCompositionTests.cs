using Chickensoft.AutoInject;
using Godot;
using Moq;
using Mortz.Client.Menus;
using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Tests.Client.Menus;

[Collection(nameof(MortzGodotCollection))]
public class ServerBrowserCompositionTests
{
    [Fact]
    public void BrowserSceneForwardsActionsAndRendersControllerResults()
    {
        Mock<IServerProbe> probe = new();
        List<FavoriteServer> saved = [];
        using ServerBrowserController controller = new(probe.Object, () => saved,
            favorites => saved = favorites.ToList());
        ServerBrowser browser = Instantiate<ServerBrowser>("res://src/Shared/UI/Menus/ServerBrowser.tscn");
        browser.FakeDependency(controller);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(browser);
        try
        {
            Assert.Empty(browser.GetChildren().OfType<ServerProbe>());
            browser.Open();
            browser.OnDirectConnectPressed();
            DirectConnectPanel panel = browser.GetNode<DirectConnectPanel>("Margin/Column/DirectPanel");
            panel.GetNode<LineEdit>("DirectMargin/DirectColumn/Fields/Address").Text = "example.test";
            panel.GetNode<LineEdit>("DirectMargin/DirectColumn/Fields/Port").Text = "30000";
            panel.GetNode<LineEdit>("DirectMargin/DirectColumn/Fields/QueryPort").Text = "28000";
            panel.GetNode<Button>("DirectMargin/DirectColumn/DirectActions/Find").EmitSignal(Button.SignalName.Pressed);
            ServerEndpoint endpoint = new("example.test", 30000, 28000);
            probe.Verify(value => value.Probe(endpoint), Times.Once);
            ServerInfo info = new("Basement Box", "deathmatch", "Arena", 3, 8, true, true,
                7777, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH);
            probe.Raise(value => value.Replied += null, new ServerProbeReply(endpoint, info, 24));
            Assert.False(panel.Visible);
            Assert.Contains("Found example.test:30000", browser.GetNode<Label>("Margin/Column/Status").Text);
            ServerRow row = browser.GetNode<Container>("Margin/Column/Scroll/Rows")
                .GetChildren().OfType<ServerRow>()
                .Single(value => Label(value, "Margin/Row/Text/Name").Text == "Basement Box");
            Assert.Equal("3/8", Label(row, "Margin/Row/Population").Text);
            browser.OnFavoritePressed();
            Assert.Equal(28000, Assert.Single(saved).QueryPort);
            (string, int)? joined = null;
            controller.JoinRequested += request => joined = (request.Address, request.Port);
            browser.OnJoinPressed();
            Assert.Equal(("example.test", 30000), joined);
            probe.Invocations.Clear();
            browser.Hide();
            probe.Verify(value => value.Cancel(), Times.Once);
        }
        finally
        {
            browser.Free();
        }
    }

    [Fact]
    public void RowShowsName_Population_AndPing_WhenOnline()
    {
        ServerRow row = Instantiate<ServerRow>("res://src/Shared/UI/Menus/ServerRow.tscn");
        ServerEntry entry = new(new ServerEndpoint("10.0.0.7", 7777), ServerSource.FAVORITE);
        entry.Info = new ServerInfo("Basement Box", "Kills", "castlewars", Players: 3,
            MaxPlayers: 8, InLobby: false, AllowJoinInProgress: false, GamePort: 7777,
            NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH);
        entry.Status = ServerStatus.ONLINE;
        entry.PingMs = 24;

        row.Bind(entry);

        Assert.Equal("Basement Box", Label(row, "Margin/Row/Text/Name").Text);
        Assert.Equal("3/8", Label(row, "Margin/Row/Population").Text);
        Assert.Equal("24 ms", Label(row, "Margin/Row/Ping").Text);
        Assert.Contains("in match", Label(row, "Margin/Row/Text/Detail").Text);
        Assert.Contains("10.0.0.7:7777", Label(row, "Margin/Row/Text/Detail").Text);
        Assert.Equal("★", row.GetNode<Button>("Margin/Row/Star").Text);
        row.Free();
    }

    [Fact]
    public void OfflineRowDropsThePingAndSaysSo()
    {
        ServerRow row = Instantiate<ServerRow>("res://src/Shared/UI/Menus/ServerRow.tscn");
        ServerEntry entry = new(new ServerEndpoint("10.0.0.7", 7777), ServerSource.DIRECT)
        {
            Status = ServerStatus.OFFLINE,
        };

        row.Bind(entry);

        Assert.Equal("", Label(row, "Margin/Row/Ping").Text);
        Assert.Equal("-", Label(row, "Margin/Row/Population").Text);
        // Unnamed: the title is already the address, so the detail is status only.
        Assert.Equal("10.0.0.7:7777", Label(row, "Margin/Row/Text/Name").Text);
        Assert.Equal("no response", Label(row, "Margin/Row/Text/Detail").Text);
        Assert.Equal("☆", row.GetNode<Button>("Margin/Row/Star").Text);
        row.Free();
    }

    [Fact]
    public void PinnedRowCannotBeUnstarred()
    {
        ServerRow row = Instantiate<ServerRow>("res://src/Shared/UI/Menus/ServerRow.tscn");

        row.Bind(new ServerEntry(ServerList.PinnedEndpoint, ServerSource.PINNED, "Mortz Playtest"));

        Button star = row.GetNode<Button>("Margin/Row/Star");
        Assert.True(star.Disabled);
        Assert.Equal("★", star.Text);
        Assert.Equal("Mortz Playtest", Label(row, "Margin/Row/Text/Name").Text);
        row.Free();
    }

    private static Label Label(Node row, string path) => row.GetNode<Label>(path);

    private static T Instantiate<T>(string path) where T : Node =>
        ResourceLoader.Load<PackedScene>(path).Instantiate<T>();
}
