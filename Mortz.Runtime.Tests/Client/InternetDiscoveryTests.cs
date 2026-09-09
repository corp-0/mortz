using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class InternetDiscoveryTests
{
    private static readonly ServerEndpoint _endpoint = new("192.0.2.1", 17000, 27000);
    private static ServerInfo Info() => new("Public", "deathmatch", "Arena", 3, 8, true, true,
        17000, NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH);

    [Fact]
    public void InternetResultIsProbedAndJoinUsesGameEndpointAndKnownRecipient()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        discovery.Last.Found(new(_endpoint, 123));
        Assert.Contains(_endpoint, probe.Requests);
        probe.Reply(_endpoint, Info());
        browser.Select(_endpoint);
        ServerJoinRequest? joined = null;
        browser.JoinRequested += request => joined = request;
        browser.JoinSelected();
        Assert.Equal(new ServerJoinRequest(_endpoint.Address, _endpoint.Port, 123), joined);
        Assert.Equal(123UL, joined!.Value.KnownSteamAccountId);
        Assert.True(browser.Selected!.SeenOnSteam);
        Assert.Equal(3, browser.Selected.Info!.Players);
        browser.JoinDirect("other.test", "7777");
        Assert.Equal(0UL, joined!.Value.KnownSteamAccountId);
    }

    [Fact]
    public void RefreshCloseAndCompletionReleaseOwnershipAndIgnoreLateResults()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        Request old = discovery.Last;
        browser.Refresh();
        Assert.Equal(1, old.Disposals);
        old.Found(new(_endpoint, 123));
        Assert.DoesNotContain(browser.Entries, entry => entry.SeenOnSteam);
        discovery.Last.Found(new(_endpoint, 123));
        discovery.Last.Completed(InternetDiscoveryResult.COMPLETE);
        Assert.Equal(1, discovery.Last.Disposals);
        Assert.Equal(InternetDiscoveryState.COMPLETE, browser.InternetState);
        discovery.Last.Found(new(new("192.0.2.2", 7777), 124));
        Assert.Equal(2, browser.Entries.Count);
        browser.Refresh();
        Request closing = discovery.Last;
        browser.Close();
        closing.Found(new(_endpoint, 123));
        Assert.Equal(1, closing.Disposals);
        Assert.DoesNotContain(browser.Entries, entry => entry.SeenOnSteam);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlineAndOutageKeepFavoritesLanAndBlindJoinAvailable(bool outage)
    {
        ulong now = 0;
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [new("saved.test", 7777)], _ => { }, discovery, () => now);
        browser.Open();
        if (outage) { discovery.Available = false; }
        else { now = ServerBrowserController.INTERNET_TIMEOUT_MS; }
        discovery.Last.Found(new(_endpoint, 123));
        Assert.Equal(outage ? InternetDiscoveryState.UNAVAILABLE : InternetDiscoveryState.TIMED_OUT, browser.InternetState);
        Assert.Equal(1, discovery.Last.Disposals);
        Assert.Contains(browser.Entries, entry => entry.Endpoint.Address == "saved.test");
        probe.Discover(_endpoint, Info());
        Assert.Contains(browser.Entries, entry => entry.SeenOnLan);
        bool joined = false;
        browser.JoinRequested += _ => joined = true;
        browser.JoinDirect("saved.test", "7777");
        Assert.True(joined);
        discovery.Available = true;
        browser.Advance();
        if (outage) { Assert.Equal(InternetDiscoveryState.READY, browser.InternetState); }
    }

    [Fact]
    public void DnsConfirmedAliasRetainsSavedHostnameQueryPortLabelAndSteamIdentity()
    {
        ServerEndpoint saved = new("Saved.Example.", 17000, 28000);
        ServerList list = new([new(saved.Address, saved.Port, "Friday", saved.QueryPort)]);
        list.ApplyInternet(new(_endpoint, 123));
        list.AddDiscovered(_endpoint);
        list.ConfirmResolved(saved, _endpoint.Address);
        list.ApplyReply(_endpoint, Info(), 20);
        ServerEntry entry = list.Find(_endpoint)!;
        Assert.Equal(saved, entry.Endpoint);
        Assert.Equal(123UL, entry.SteamAccountId);
        Assert.True(entry.SeenOnLan);
        Assert.True(entry.SeenOnSteam);
        Assert.Equal("Friday", Assert.Single(list.Favorites).Label);
        Assert.Equal(28000, Assert.Single(list.Favorites).QueryPort);
        Assert.Equal(2, list.Entries.Count);
        list.MarkProbing();
        Assert.False(entry.SeenOnSteam);
        Assert.False(entry.SeenOnLan);
        Assert.Equal(0UL, entry.SteamAccountId);
        Assert.Single(list.Favorites);
    }

    [Fact]
    public void DiscoveryBudgetDeduplicatesAndNeverDiscardsFavorites()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [new("saved.test", 7777)], _ => { }, discovery, () => 0);
        browser.Open();
        for (int i = 0; i < 1100; i++)
        {
            discovery.Last.Found(new(new($"192.0.{i / 256}.{i % 256}", 7777), (ulong)i));
        }
        Assert.Equal(1002, browser.Entries.Count);
        Assert.Equal(1002, probe.Requests.Count);
        Assert.Contains(browser.Entries, entry => entry.Endpoint.Address == "saved.test");
    }

    [Fact]
    public void SynchronousCompletionDisposesReturnedRequestExactlyOnce()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new() { CompleteSynchronously = true };
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        Assert.Equal(InternetDiscoveryState.COMPLETE, browser.InternetState);
        Assert.Equal(1, discovery.Last.Disposals);
        browser.Close();
        Assert.Equal(1, discovery.Last.Disposals);
    }

    [Fact]
    public void RequestFailureDoesNotPreventLanFavoritesOrDirectJoining()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new() { Throw = true };
        using ServerBrowserController browser = new(probe, () => [new("saved.test", 7777)], _ => { }, discovery, () => 0);
        browser.Open();
        Assert.Equal(InternetDiscoveryState.FAILED, browser.InternetState);
        Assert.Equal(1, probe.LanStarts);
        Assert.Contains(browser.Entries, entry => entry.IsFavorite);
        ServerJoinRequest? joined = null;
        browser.JoinRequested += request => joined = request;
        browser.JoinDirect("saved.test", "7777");
        Assert.Equal(new ServerJoinRequest("saved.test", 7777), joined);
    }

    [Fact]
    public void CompletionAtDeadlineIsTimeoutAndCannotReviveARefresh()
    {
        ulong now = 0;
        Discovery discovery = new();
        using ServerBrowserController browser = new(new ServerBrowserControllerTests.FakeProbe(),
            () => [], _ => { }, discovery, () => now);
        browser.Open();
        now = ServerBrowserController.INTERNET_TIMEOUT_MS;
        discovery.Last.Completed(InternetDiscoveryResult.COMPLETE);
        Assert.Equal(InternetDiscoveryState.TIMED_OUT, browser.InternetState);
        Assert.Equal(1, discovery.Last.Disposals);
    }

    [Fact]
    public void LanAndSteamShareTransientBudgetButFavoritesCanStillBeEnriched()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [new(_endpoint.Address, _endpoint.Port, "Saved", _endpoint.QueryPort)],
            _ => { }, discovery, () => 0);
        browser.Open();
        for (int i = 0; i < 700; i++)
        {
            probe.Discover(new($"10.0.{i / 256}.{i % 256}", 7777), Info());
        }
        for (int i = 0; i < 700; i++)
        {
            discovery.Last.Found(new(new($"10.1.{i / 256}.{i % 256}", 7777), (ulong)i));
        }
        discovery.Last.Found(new(_endpoint, 123));
        Assert.Equal(1002, browser.Entries.Count);
        Assert.Equal(123UL, browser.Entries.Single(entry => entry.Endpoint == _endpoint).SteamAccountId);
        Assert.Equal(303, probe.Requests.Count);
    }

    [Fact]
    public void ResolvedFavoriteMergesLaterSteamAndLanWithoutLosingItsQueryEndpoint()
    {
        ServerEndpoint saved = new("saved.example", _endpoint.Port, 28000);
        ServerList list = new([new(saved.Address, saved.Port, "Friday", saved.QueryPort)]);
        list.ConfirmResolved(saved, _endpoint.Address);
        list.ApplyInternet(new(_endpoint, 123));
        list.AddDiscovered(_endpoint);
        list.ApplyReply(saved, Info(), 20);
        list.ApplyTimeout(_endpoint);
        ServerEntry entry = list.Find(_endpoint)!;
        Assert.Equal(2, list.Entries.Count);
        Assert.Equal(saved, entry.Endpoint);
        Assert.Equal(ServerStatus.ONLINE, entry.Status);
        Assert.True(entry.SeenOnSteam && entry.SeenOnLan);
        Assert.Equal(123UL, entry.SteamAccountId);
    }

    [Fact]
    public void ResultBatchesNotifyPresentationOncePerAdvance()
    {
        ServerBrowserControllerTests.FakeProbe probe = new();
        Discovery discovery = new();
        using ServerBrowserController browser = new(probe, () => [], _ => { }, discovery, () => 0);
        browser.Open();
        int changes = 0;
        browser.Changed += () => changes++;
        for (int i = 0; i < 100; i++)
        {
            ServerEndpoint endpoint = new($"192.0.2.{i}", 7777);
            discovery.Last.Found(new(endpoint, (ulong)i));
            probe.Reply(endpoint, Info());
        }
        Assert.Equal(0, changes);
        browser.Advance();
        Assert.Equal(1, changes);
        browser.Advance();
        Assert.Equal(1, changes);
    }

    private class Discovery : IInternetDiscovery
    {
        public bool Available { get; set; } = true;
        public Request Last { get; private set; } = null!;
        public bool CompleteSynchronously;
        public bool Throw;
        public IDisposable Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed)
        {
            if (Throw) { throw new InvalidOperationException("Unavailable"); }
            Last = new(found, completed);
            if (CompleteSynchronously) { completed(InternetDiscoveryResult.COMPLETE); }
            return Last;
        }
    }

    private class Request(Action<InternetServer> found, Action<InternetDiscoveryResult> completed) : IDisposable
    {
        public Action<InternetServer> Found => found;
        public Action<InternetDiscoveryResult> Completed => completed;
        public int Disposals;
        public void Dispose() => Disposals++;
    }
}
