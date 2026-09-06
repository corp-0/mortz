using Mortz.Client.Session;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class DelayedConnectionWorkTests
{
    [Fact]
    public void ResetDiscardsTrafficFromThePreviousConnection()
    {
        DelayedConnectionWork work = new();
        List<string> delivered = [];
        work.Schedule(10, () => delivered.Add("old input"));
        work.Schedule(10, () => delivered.Add("old snapshot"));
        work.Reset();
        work.Schedule(20, () => delivered.Add("new connection"));
        work.Advance(15);
        Assert.Empty(delivered);
        work.Advance(20);
        Assert.Equal(["new connection"], delivered);
    }

    [Fact]
    public void DisconnectInsideAHandlerStopsTheRemainingCallbacks()
    {
        DelayedConnectionWork work = new();
        work.Schedule(10, work.Reset);
        work.Schedule(10, () => Assert.Fail("A closed connection delivered another packet."));
        work.Advance(10);
    }
}
