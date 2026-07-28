using System.Reflection;
using Godot;
using Mortz.Client.Menus;
using Mortz.Client.Servers;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Xunit;

namespace Mortz.Tests.Client.Menus;

[Collection(nameof(MortzGodotCollection))]
public class ServerBrowserCompositionTests
{
    [Fact]
    public void BrowserSceneResolvesEveryExport()
    {
        AssertExportsResolved<ServerBrowser>("res://src/Shared/UI/Menus/ServerBrowser.tscn");
    }

    [Fact]
    public void RowSceneResolvesEveryExport()
    {
        AssertExportsResolved<ServerRow>("res://src/Shared/UI/Menus/ServerRow.tscn");
    }

    [Fact]
    public void DirectPanelSceneResolvesEveryExport()
    {
        AssertExportsResolved<DirectConnectPanel>("res://src/Shared/UI/Menus/DirectConnectPanel.tscn");
    }

    [Fact]
    public void SettingsSceneResolvesEveryExport()
    {
        AssertExportsResolved<SettingsScreen>("res://src/Shared/UI/Menus/Settings.tscn");
    }

    [Fact]
    public void MenuSceneResolvesEveryExport()
    {
        AssertExportsResolved<MainMenu>("res://src/Shared/UI/Menus/MainMenu.tscn");
    }

    [Fact]
    public void RowShowsName_Population_AndPing_WhenOnline()
    {
        ServerRow row = Instantiate<ServerRow>("res://src/Shared/UI/Menus/ServerRow.tscn");
        ServerEntry entry = new(new ServerEndpoint("10.0.0.7", 7777), ServerSource.FAVORITE);
        entry.Info = new ServerInfo("Basement Box", "Kills", "castlewars", Players: 3,
            MaxPlayers: 8, InLobby: false, GamePort: 7777,
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

        row.Bind(new ServerEntry(ServerList.PINNED_ENDPOINT, ServerSource.PINNED, "Mortz Playtest"));

        Button star = row.GetNode<Button>("Margin/Row/Star");
        Assert.True(star.Disabled);
        Assert.Equal("★", star.Text);
        Assert.Equal("Mortz Playtest", Label(row, "Margin/Row/Text/Name").Text);
        row.Free();
    }

    private static Label Label(Node row, string path) => row.GetNode<Label>(path);

    private static T Instantiate<T>(string path) where T : Node =>
        ResourceLoader.Load<PackedScene>(path).Instantiate<T>();

    /// <summary>Exports are assigned from the scene's NodePaths, so a typo in
    /// a hand-written .tscn shows up as a null here rather than as a crash the
    /// first time a button is pressed.</summary>
    private static void AssertExportsResolved<T>(string path) where T : Node
    {
        T node = Instantiate<T>(path);
        foreach (FieldInfo field in typeof(T)
                     .GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                BindingFlags.Public)
                     .Where(field => field.IsDefined(typeof(ExportAttribute))))
        {
            Assert.True(field.GetValue(node) != null, $"{typeof(T).Name}.{field.Name} is unwired");
        }
        node.Free();
    }
}
