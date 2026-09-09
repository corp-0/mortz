using Mortz.Protocol.Net.Query;
using Mortz.Server.Platform;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class ServerPublicationTests
{
    private static ServerInfo Info(int players = 0) => new("Server", "deathmatch", "Arena", players, 8,
        true, true, 7777, 47, 1);

    [Fact]
    public void MetadataChangesCoalesceAndPublicationRequiresPublicLoggedOnServer()
    {
        Backend backend = new();
        using ServerPublication publication = new(backend);
        publication.Advance(Info(), [], false, true, 0);
        Assert.False(publication.Active);
        publication.Advance(Info(1), [], true, true, 10);
        Assert.True(publication.Active);
        publication.Advance(Info(2), [], true, true, 999);
        Assert.Single(backend.Published);
        publication.Advance(Info(3), [], true, true, 1000);
        Assert.Equal(3, backend.Published[1].Players);
        publication.Advance(Info(3), [], true, true, 2000);
        Assert.Equal(2, backend.Published.Count);
        publication.Advance(Info(3), [], false, true, 2001);
        Assert.False(publication.Active);
        publication.Advance(Info(3), [], true, false, 2002);
        Assert.False(publication.Active);
        publication.Advance(Info(3), [], true, true, 2003);
        publication.Dispose();
        Assert.False(publication.Active);
        Assert.Equal([true, false, true, false], backend.Advertising);
    }

    private class Backend : IServerPublication
    {
        public List<ServerInfo> Published { get; } = [];
        public List<bool> Advertising { get; } = [];
        public void Publish(ServerInfo info) => Published.Add(info);
        public void Advertise(bool active) => Advertising.Add(active);
        public ulong CreateGuest() => 0;
        public bool Update(ulong accountId, string name) => true;
        public void EndGuest(ulong accountId) { }
    }
}
