using Mortz.Core.Features;
using Xunit;

namespace Mortz.Runtime.Tests.Core;

public class FeatureScopeTests
{
    private class Feature(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    [Fact]
    public void ClosureStopsRoutingBeforeStateAndKeepsOwnersAliveUntilStateIsReleased()
    {
        List<string> events = [];
        FeatureScope scope = new(_ => events.Add("route"), _ => events.Add("unroute"));
        scope.Register(new Feature(() => events.Add("feature")));
        scope.OwnState(() => events.Add("state"), () => "Feature: State");
        using CancellationTokenRegistration cancellation = scope.Lifetime.Register(() => events.Add("cancel"));
        scope.Start();
        scope.Dispose();
        scope.Dispose();
        Assert.Equal(["route", "unroute", "cancel", "state", "feature"], events);
        Assert.True(scope.Lifetime.IsCancellationRequested);
        Assert.Contains("Feature: State", scope.Describe());
    }

    [Fact]
    public void OneFailingCleanupDoesNotLeaveSensitiveFeaturesAlive()
    {
        bool disposed = false;
        FeatureScope scope = new();
        scope.Register(new Feature(() => disposed = true));
        scope.Own(() => throw new InvalidOperationException("state failure"));
        scope.Start();
        Assert.Throws<AggregateException>(scope.Dispose);
        Assert.True(disposed);
        Assert.True(scope.IsClosed);
    }
}
