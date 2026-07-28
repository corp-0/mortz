using Mortz.Client.Servers;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;
using Xunit;

namespace Mortz.Tests.Client;

public class ServerListTests
{
    private static readonly ServerEndpoint _lan = new("192.168.1.20", 7777);

    private static ServerInfo Info(int protocolVersion = NetConfig.PROTOCOL_VERSION,
        ulong schemaHash = 0, string name = "Someone's Server") =>
        new(name, "Kills", "castlewars", Players: 2, MaxPlayers: 8, InLobby: true,
            GamePort: 7777, protocolVersion,
            schemaHash == 0 ? NetRegistry.SCHEMA_HASH : schemaHash);

    [Fact]
    public void FreshListStartsWithThePinnedServerOnly()
    {
        ServerList list = new();

        ServerEntry pinned = Assert.Single(list.Entries);
        Assert.Equal(ServerList.PINNED_ENDPOINT, pinned.Endpoint);
        Assert.Equal(ServerSource.PINNED, pinned.Source);
        Assert.True(pinned.IsFavorite);
        Assert.Empty(list.Favorites);
    }

    [Fact]
    public void PinnedServerCannotBeUnfavorited()
    {
        ServerList list = new();
        ServerEntry pinned = list.Entries[0];

        Assert.False(pinned.CanToggleFavorite);
        Assert.False(list.ToggleFavorite(pinned));
        Assert.Equal(ServerSource.PINNED, pinned.Source);
    }

    [Fact]
    public void SavedFavoritesAreRestoredAndAPinnedDuplicateIsNotListedTwice()
    {
        ServerList list = new([
            new FavoriteServer("box.example.com", 7777, "Weekly game"),
            new FavoriteServer(ServerList.PINNED_ENDPOINT.Address, ServerList.PINNED_ENDPOINT.Port),
        ]);

        Assert.Equal(2, list.Entries.Count);
        FavoriteServer saved = Assert.Single(list.Favorites);
        Assert.Equal("box.example.com", saved.Address);
        Assert.Equal("Weekly game", saved.Label);
    }

    [Fact]
    public void DirectConnectFindsAreSessionOnlyUntilStarred()
    {
        ServerList list = new();

        ServerEntry entry = list.AddDirect(_lan);
        Assert.Empty(list.Favorites);

        Assert.True(list.ToggleFavorite(entry));

        Assert.Equal(_lan.Address, Assert.Single(list.Favorites).Address);
    }

    [Fact]
    public void UnstarringKeepsTheRowButStopsSavingIt()
    {
        ServerList list = new([new FavoriteServer(_lan.Address, _lan.Port)]);
        ServerEntry entry = list.Find(_lan)!;

        Assert.True(list.ToggleFavorite(entry));

        Assert.Empty(list.Favorites);
        Assert.Contains(list.Entries, listed => listed.Endpoint == _lan);
        Assert.False(entry.IsFavorite);
    }

    [Fact]
    public void DiscoveryDoesNotDemoteAnAlreadyFavoritedServer()
    {
        ServerList list = new([new FavoriteServer(_lan.Address, _lan.Port)]);

        list.AddDiscovered(_lan);

        Assert.Equal(ServerSource.FAVORITE, list.Find(_lan)!.Source);
        Assert.Equal(2, list.Entries.Count);
    }

    [Fact]
    public void AddressMatchingIgnoresHostnameCase()
    {
        ServerList list = new([new FavoriteServer("Box.Example.Com", 7777)]);

        Assert.NotNull(list.Find(new ServerEndpoint("box.example.com", 7777)));
        Assert.Null(list.Find(new ServerEndpoint("box.example.com", 7778)));
    }

    [Fact]
    public void ReplyMarksOnlineWithPingAndPopulation()
    {
        ServerList list = new();

        list.MarkProbing();
        Assert.Equal(ServerStatus.PROBING, list.Entries[0].Status);

        list.ApplyReply(ServerList.PINNED_ENDPOINT, Info(), pingMs: 42);

        ServerEntry pinned = list.Entries[0];
        Assert.Equal(ServerStatus.ONLINE, pinned.Status);
        Assert.Equal(42, pinned.PingMs);
        Assert.Equal(2, pinned.Info!.Players);
        Assert.Equal("Someone's Server", pinned.DisplayName);
    }

    [Fact]
    public void MismatchedBuildReadsAsIncompatibleNotDead()
    {
        ServerList list = new();

        list.ApplyReply(ServerList.PINNED_ENDPOINT,
            Info(protocolVersion: NetConfig.PROTOCOL_VERSION + 1), pingMs: 10);

        Assert.Equal(ServerStatus.INCOMPATIBLE, list.Entries[0].Status);
    }

    [Fact]
    public void MismatchedSchemaHashIsAlsoIncompatible()
    {
        ServerList list = new();

        list.ApplyReply(ServerList.PINNED_ENDPOINT, Info(schemaHash: 0xBADF00D), pingMs: 10);

        Assert.Equal(ServerStatus.INCOMPATIBLE, list.Entries[0].Status);
    }

    [Fact]
    public void TimeoutClearsTheStalePing()
    {
        ServerList list = new();
        list.ApplyReply(ServerList.PINNED_ENDPOINT, Info(), pingMs: 42);

        list.ApplyTimeout(ServerList.PINNED_ENDPOINT);

        Assert.Equal(ServerStatus.OFFLINE, list.Entries[0].Status);
        Assert.Equal(0, list.Entries[0].PingMs);
    }

    [Fact]
    public void UnnamedEntriesFallBackToLabelThenAddress()
    {
        ServerList list = new([new FavoriteServer(_lan.Address, _lan.Port, "Basement box")]);
        ServerEntry labelled = list.Find(_lan)!;
        ServerEntry bare = list.AddDirect(new ServerEndpoint("10.0.0.5", 7777));

        Assert.Equal("Basement box", labelled.DisplayName);
        Assert.Equal("10.0.0.5:7777", bare.DisplayName);
    }

    [Fact]
    public void EntriesAreGroupedPinnedFavoriteLanDirect()
    {
        ServerList list = new([new FavoriteServer("box.example.com", 7777)]);
        list.AddDirect(new ServerEndpoint("10.0.0.5", 7777));
        list.AddDiscovered(_lan);

        Assert.Equal(
            [ServerSource.PINNED, ServerSource.FAVORITE, ServerSource.LAN, ServerSource.DIRECT],
            list.Entries.Select(entry => entry.Source));
    }

    /// <summary>Grouping comes from SortRank, not the enum's numeric values,
    /// so reordering ServerSource cannot reorder the browser.</summary>
    [Fact]
    public void DisplayOrderComesFromTheSortRank()
    {
        ServerList list = new([new FavoriteServer("box.example.com", 7777)]);
        list.AddDirect(new ServerEndpoint("10.0.0.5", 7777));
        list.AddDiscovered(_lan);

        Assert.Equal([0, 1, 2, 3], list.Entries.Select(entry => entry.SortRank));
    }

    [Fact]
    public void QueryPortSitsBesideTheJoinPort()
    {
        Assert.Equal(7778, new ServerEndpoint("host", 7777).QueryPort);
    }
}
