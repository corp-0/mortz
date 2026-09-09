using Mortz.Client.Servers;
using Mortz.Protocol.Net.Query;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ServerProbeCoordinatorTests
{
    [Fact]
    public void Probes_AreLimitedToSixteenConcurrentExchanges()
    {
        ServerProbeCoordinator coordinator = new();
        for (int i = 0; i < 17; i++)
        {
            coordinator.Schedule(new ServerEndpoint($"server{i}.example", 7777));
        }
        List<ServerProbeWork> active = [];
        while (coordinator.TryStartNext(0, out ServerProbeWork work))
        {
            active.Add(work);
        }
        Assert.Equal(16, active.Count);
        coordinator.Complete(active[0], null);
        Assert.True(coordinator.TryStartNext(50, out _));
    }

    [Fact]
    public void Cancellation_DropsQueueLanWindowAndLateCompletions()
    {
        ServerProbeCoordinator coordinator = new();
        int completed = 0;
        coordinator.TimedOut += _ => completed++;
        coordinator.Schedule(new ServerEndpoint("first.example", 7777));
        coordinator.Schedule(new ServerEndpoint("second.example", 7777));
        coordinator.BeginLan(0);
        Assert.True(coordinator.TryStartNext(0, out ServerProbeWork old));
        coordinator.Cancel();
        Assert.False(coordinator.IsLanActive(1));
        Assert.False(coordinator.TryStartNext(1, out _));
        coordinator.Schedule(old.Endpoint);
        Assert.True(coordinator.TryStartNext(2, out ServerProbeWork current));
        coordinator.Complete(old, null);
        Assert.Equal(0, completed);
        Assert.True(coordinator.IsActive(current));
        coordinator.Complete(current, null);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Discovery_DeduplicatesCompletedEndpointsThroughoutRefresh()
    {
        ServerProbeCoordinator coordinator = new();
        coordinator.BeginLan(0);
        byte[] packet = ServerQueryProtocol.EncodeChallenge(42);
        coordinator.ObserveLan(packet, "192.0.2.1", 7778, 1);
        Assert.True(coordinator.TryStartNext(1, out ServerProbeWork work));
        coordinator.Complete(work, null);
        coordinator.ObserveLan(packet, "192.0.2.1", 7778, 10);
        Assert.False(coordinator.TryStartNext(10, out _));
        coordinator.BeginLan(20);
        coordinator.ObserveLan(packet, "192.0.2.1", 7778, 21);
        Assert.False(coordinator.TryStartNext(21, out _));
        coordinator.Cancel();
        coordinator.BeginLan(30);
        coordinator.ObserveLan(packet, "192.0.2.1", 7778, 31);
        Assert.True(coordinator.TryStartNext(31, out _));
    }

    [Fact]
    public void Discovery_TotalRefreshBoundIncludesCompletedResultsAndPreservesExplicitRequests()
    {
        ServerProbeCoordinator coordinator = new();
        coordinator.BeginLan(0);
        byte[] packet = ServerQueryProtocol.EncodeChallenge(42);
        for (int i = 0; i < 1001; i++)
        {
            coordinator.ObserveLan(packet, $"10.0.{i / 256}.{i % 256}", 7778, 1);
            if (i < 1000)
            {
                Assert.True(coordinator.TryStartNext(1, out ServerProbeWork work));
                coordinator.Complete(work, null);
            }
        }
        Assert.False(coordinator.TryStartNext(1, out _));
        coordinator.Schedule(new ServerEndpoint("favorite.example", 7777));
        Assert.True(coordinator.TryStartNext(2, out ServerProbeWork favorite));
        Assert.False(favorite.Discovered);
    }

    [Fact]
    public void Discovery_AcceptsOnlyRecognizedPacketsInsideActiveWindow()
    {
        ServerProbeCoordinator coordinator = new();
        byte[] packet = ServerQueryProtocol.EncodeChallenge(42);
        coordinator.ObserveLan(packet, "192.0.2.1", 7778, 1);
        coordinator.BeginLan(10);
        coordinator.ObserveLan(packet, "192.0.2.1", 9999, 11);
        coordinator.ObserveLan([1, 2, 3], "192.0.2.1", 7778, 12);
        coordinator.ObserveLan(packet, "192.0.2.1", 7778, 3010);
        Assert.False(coordinator.TryStartNext(3010, out _));
    }
}
