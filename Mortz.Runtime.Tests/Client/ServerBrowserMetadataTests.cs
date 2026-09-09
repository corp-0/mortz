using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class ServerBrowserMetadataTests
{
    [Fact]
    public void FavoritesRetainExplicitQueryPortAndLabel()
    {
        ServerList list = new([new FavoriteServer("box.example", 17000, "Friday", 27000)]);
        ServerEndpoint endpoint = new("box.example", 17000, 27000);
        list.ApplyReply(endpoint, Info(), 20);
        FavoriteServer favorite = Assert.Single(list.Favorites);
        Assert.Equal(27000, favorite.QueryPort);
        Assert.Equal("Friday", favorite.Label);
        Assert.Equal(endpoint, list.Find(endpoint)!.Endpoint);
    }

    [Fact]
    public void LegacyFavoritesDefaultQueryPortAndOverflowIsRejected()
    {
        ServerList list = new([new FavoriteServer("legacy", 7777), new FavoriteServer("overflow", 65535)]);
        Assert.Equal(7778, list.Find(new ServerEndpoint("legacy", 7777))!.Endpoint.QueryPort);
        Assert.Single(list.Favorites);
    }

    [Fact]
    public void MissingRulesAreUnknownWhileWrongSchemaIsIncompatible()
    {
        ServerList list = new();
        ServerEndpoint endpoint = new("127.0.0.1", 7777);
        list.ApplyReply(endpoint, Info() with { MetadataValid = false }, 10);
        Assert.Equal(ServerStatus.UNKNOWN, list.Find(endpoint)!.Status);
        list.ApplyReply(endpoint, Info() with { SchemaHash = NetRegistry.SCHEMA_HASH ^ 1 }, 10);
        Assert.Equal(ServerStatus.INCOMPATIBLE, list.Find(endpoint)!.Status);
    }

    [Fact]
    public void DiscoveryLimitPreservesFavoritesAndExistingEntries()
    {
        ServerList list = new([new FavoriteServer("favorite", 7777)]);
        for (int i = 0; i < 1001; i++)
        {
            list.ApplyReply(new ServerEndpoint($"server-{i}", 7777), Info(), 10);
        }
        Assert.Equal(1002, list.Entries.Count);
        ServerEndpoint favorite = new("favorite", 7777);
        list.ApplyReply(favorite, Info(), 10);
        Assert.Equal(ServerStatus.ONLINE, list.Find(favorite)!.Status);
        Assert.Single(list.Favorites);
    }

    private static ServerInfo Info() => new("Server", "deathmatch", "duel", 1, 8, true, true,
        7777, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH, NetConfig.GAME_APP_ID);
}
