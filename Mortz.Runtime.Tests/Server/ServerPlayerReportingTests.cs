using Mortz.Core.Identity;
using Mortz.Server.Platform;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class ServerPlayerReportingTests
{
    private static Player Guest(int peer = 2) => new(peer, "Guest", 0, 1);

    [Fact]
    public void MixedPopulationOwnsOnlyGuestRecordsAndReleasesEachOnce()
    {
        Backend backend = new();
        using ServerPlayerReporting reporting = new(backend);
        Player guest = Guest();
        Player verified = new(3, "Steam player", 0, 1, account: new(AccountProvider.STEAM, 42));
        reporting.Update([guest, verified], true);
        reporting.Update([guest, verified], true);
        Assert.Equal(1, backend.Created);
        Assert.Contains((42UL, "Steam player"), backend.Updates);
        Assert.Contains((101UL, "Guest"), backend.Updates);
        reporting.Update([verified], true);
        reporting.Dispose();
        reporting.Dispose();
        Assert.Equal([101UL], backend.Ended);
        Assert.Null(guest.Account);
    }

    [Fact]
    public void PeerReuseAndCapabilityResetCreateFreshRecords()
    {
        Backend backend = new();
        using ServerPlayerReporting reporting = new(backend);
        Player old = Guest(), replacement = Guest();
        reporting.Update([old], true);
        reporting.Update([replacement], true);
        Assert.Equal([101UL], backend.Ended);
        reporting.Update([replacement], false);
        reporting.Update([replacement], false);
        reporting.Update([replacement], true);
        Assert.Equal(3, backend.Created);
        Assert.Equal([101UL, 102UL], backend.Ended);
    }

    [Fact]
    public void PartialCreationAndUpdateFailureDoNotLeakOrDuplicateRecords()
    {
        Backend backend = new() { FailCreate = true };
        using ServerPlayerReporting reporting = new(backend);
        Player guest = Guest();
        reporting.Update([guest], true);
        Assert.Empty(backend.Updates);
        Assert.Empty(backend.Ended);
        backend.FailCreate = false;
        backend.FailUpdate = true;
        reporting.Update([guest], true);
        Assert.Equal([102UL], backend.Ended);
        backend.FailUpdate = false;
        reporting.Update([guest], true);
        reporting.Update([guest], true);
        Assert.Equal(3, backend.Created);
        reporting.Dispose();
        Assert.Equal([102UL, 103UL], backend.Ended);
    }

    private class Backend : IServerPlayerReporting
    {
        public int Created;
        public bool FailCreate;
        public bool FailUpdate;
        public List<ulong> Ended { get; } = [];
        public List<(ulong, string)> Updates { get; } = [];
        public ulong CreateGuest() { Created++; return FailCreate ? 0 : (ulong)(100 + Created); }
        public bool Update(ulong accountId, string name) { Updates.Add((accountId, name)); return !FailUpdate; }
        public void EndGuest(ulong accountId) => Ended.Add(accountId);
    }
}
