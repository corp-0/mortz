using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class ServerBrowserControllerTests
{
    private static ServerInfo Info() => new("Test server", "deathmatch", "Arena", 0, 8,
        true, true, 7777, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH);

    [Fact]
    public void OpenRefreshesFavoritesAndLanAndCloseIgnoresLateReplies()
    {
        FakeProbe probe = new();
        FavoriteServer favorite = new("example.test", 7777, "Saved", 28000);
        using ServerBrowserController browser = new(probe, () => [favorite], _ => { });
        browser.Open();
        Assert.Contains(new ServerEndpoint("example.test", 7777, 28000), probe.Requests);
        Assert.Equal(1, probe.LanStarts);
        Assert.All(browser.Entries, entry => Assert.Equal(ServerStatus.PROBING, entry.Status));
        int count = browser.Entries.Count;
        browser.Close();
        probe.Reply(new ServerEndpoint("10.0.0.8", 7777), Info());
        Assert.Equal(count, browser.Entries.Count);
        Assert.Equal(2, probe.Cancellations);
    }

    [Fact]
    public void AnOlderDirectReplyCannotCompleteTheCurrentFind()
    {
        FakeProbe probe = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { });
        browser.Open();
        browser.FindDirect("one.test", "7777", "28000");
        browser.FindDirect("two.test", "8888", "29000");
        ServerEndpoint current = new("two.test", 8888, 29000);
        probe.Reply(new ServerEndpoint("one.test", 7777, 28000), Info());
        Assert.Equal(DirectConnectState.PROBING, browser.DirectState);
        Assert.Equal(current, browser.DirectTarget);
        Assert.Null(browser.Selected);
        probe.Reply(current, Info());
        Assert.Equal(DirectConnectState.CLOSED, browser.DirectState);
        Assert.Equal(current, browser.Selected!.Endpoint);
        Assert.Equal(ServerSource.DIRECT, browser.Selected.Source);
    }

    [Fact]
    public void ACancelledFindCannotReopenThePanelOrChangeSelection()
    {
        FakeProbe probe = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { });
        browser.Open();
        browser.FindDirect("one.test", "7777", "28000");
        browser.CancelDirect();
        probe.Reply(new ServerEndpoint("one.test", 7777, 28000), Info());
        Assert.Equal(DirectConnectState.CLOSED, browser.DirectState);
        Assert.Null(browser.Selected);
        Assert.Equal(BrowserNotice.NONE, browser.Notice);
    }

    [Fact]
    public void TimeoutAllowsBlindJoinWithoutRequiringAQueryPort()
    {
        FakeProbe probe = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { });
        browser.Open();
        browser.FindDirect("one.test", "65535", "28000");
        probe.Timeout(new ServerEndpoint("one.test", 65535, 28000));
        Assert.Equal(DirectConnectState.NOT_FOUND, browser.DirectState);
        (string, int)? joined = null;
        browser.JoinRequested += request => joined = (request.Address, request.Port);
        browser.JoinDirect("one.test", "65535");
        Assert.Equal(("one.test", 65535), joined);
    }

    [Fact]
    public void JoinChecksCompatibilityAndPreservesTheEnteredGamePort()
    {
        FakeProbe probe = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { });
        browser.Open();
        browser.FindDirect("one.test", "30000", "28000");
        ServerEndpoint endpoint = new("one.test", 30000, 28000);
        (string, int)? joined = null;
        browser.JoinRequested += request => joined = (request.Address, request.Port);
        probe.Reply(endpoint, Info() with { ProtocolVersion = -1 });
        browser.JoinSelected();
        Assert.Null(joined);
        Assert.Equal(BrowserNotice.INCOMPATIBLE, browser.Notice);
        probe.Reply(endpoint, Info());
        browser.JoinSelected();
        Assert.Equal(("one.test", 30000), joined);
    }

    [Fact]
    public void FavoritesArePersistedByTheControllerWithTheirQueryPort()
    {
        FakeProbe probe = new();
        List<FavoriteServer> saved = [];
        using ServerBrowserController browser = new(probe, () => saved,
            favorites => saved = favorites.ToList());
        browser.Open();
        browser.FindDirect("one.test", "30000", "28000");
        ServerEndpoint endpoint = new("one.test", 30000, 28000);
        probe.Reply(endpoint, Info());
        browser.ToggleFavorite(endpoint);
        Assert.Equal(28000, Assert.Single(saved).QueryPort);
        browser.Close();
        browser.Open();
        Assert.Contains(browser.Entries, entry => entry.Endpoint == endpoint && entry.IsFavorite);
        browser.ToggleFavorite(ServerList.PinnedEndpoint);
        Assert.Single(saved);
        Assert.Equal(BrowserNotice.PINNED, browser.Notice);
    }

    [Fact]
    public void RefreshCancelsAPendingDirectFind()
    {
        FakeProbe probe = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { });
        browser.Open();
        browser.FindDirect("one.test", "7777", "");
        browser.Refresh();
        Assert.Null(browser.DirectTarget);
        Assert.Equal(DirectConnectState.EDITING, browser.DirectState);
        probe.Timeout(new ServerEndpoint("one.test", 7777));
        Assert.Equal(DirectConnectState.EDITING, browser.DirectState);
    }

    [Theory]
    [InlineData("example.test:8888", "7777", "", "example.test", 8888, 8889)]
    [InlineData("[::1]:8888", "7777", "28000", "::1", 8888, 28000)]
    [InlineData("::1", "7777", "", "::1", 7777, 7778)]
    public void AddressParsingKeepsGameAndQueryPortsSeparate(string input, string game, string query,
        string host, int gamePort, int queryPort)
    {
        Assert.True(ServerAddressInput.TryReadQuery(input, game, query, out ServerEndpoint endpoint, out _));
        Assert.Equal(new ServerEndpoint(host, gamePort, queryPort), endpoint);
    }

    [Theory]
    [InlineData("65535", "", ServerAddressError.QUERY_PORT_REQUIRED)]
    [InlineData("7777", "7777", ServerAddressError.SAME_PORT)]
    [InlineData("7777", "65536", ServerAddressError.INVALID_QUERY_PORT)]
    [InlineData("0", "", ServerAddressError.INVALID_PORT)]
    public void InvalidPortsDoNotScheduleQueries(string game, string query, ServerAddressError error)
    {
        FakeProbe probe = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { });
        browser.Open();
        int before = probe.Requests.Count;
        browser.FindDirect("example.test", game, query);
        Assert.Equal(error, browser.AddressError);
        Assert.Equal(before, probe.Requests.Count);
        Assert.Null(browser.DirectTarget);
    }

    public class FakeProbe : IServerProbe
    {
        public List<ServerEndpoint> Requests { get; } = [];
        public int Cancellations { get; private set; }
        public int LanStarts { get; private set; }
        public event Action<ServerProbeReply>? Replied;
        public event Action<ServerProbeReply>? Discovered;
        public event Action<ServerEndpoint>? TimedOut;
        public void Probe(ServerEndpoint endpoint) => Requests.Add(endpoint);
        public void Probe(IEnumerable<ServerEndpoint> endpoints) => Requests.AddRange(endpoints);
        public void DiscoverLan() => LanStarts++;
        public void Cancel() => Cancellations++;
        public void Reply(ServerEndpoint endpoint, ServerInfo info) => Replied?.Invoke(new(endpoint, info, 20));
        public void Discover(ServerEndpoint endpoint, ServerInfo info) => Discovered?.Invoke(new(endpoint, info, 20));
        public void Timeout(ServerEndpoint endpoint) => TimedOut?.Invoke(endpoint);
    }
}
