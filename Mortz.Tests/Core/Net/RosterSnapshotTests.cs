using Mortz.Core.Match;
using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class RosterSnapshotTests
{
    private static RosterEntry Row(int peerId, byte slot) =>
        new(peerId, $"P{peerId}", 0, null, new NetSlot(slot));

    [Fact]
    public void ASlotHasExactlyOneHolder()
    {
        RosterSnapshot snapshot = new([Row(11, 1), Row(22, 2)]);

        Assert.Equal(11, snapshot.PeerInSlot(new NetSlot(1)));
        Assert.Equal(22, snapshot.PeerInSlot(new NetSlot(2)));
        Assert.Null(snapshot.PeerInSlot(new NetSlot(3)));
    }

    [Fact]
    public void APeerIsFoundByItsId()
    {
        RosterSnapshot snapshot = new([new RosterEntry(11, "Alice", 4,
            Team.RED, new NetSlot(1))]);

        Assert.True(snapshot.TryFind(11, out RosterEntry entry));
        Assert.Equal("Alice", entry.Name);
        Assert.Equal(4, entry.Skin);
        Assert.False(snapshot.TryFind(22, out _));
    }

    [Fact]
    public void ADuplicatePeerIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new RosterSnapshot([Row(11, 1), Row(11, 2)]));
        Assert.False(RosterSnapshot.TryFrom([Row(11, 1), Row(11, 2)], out RosterSnapshot? snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void ADuplicateSlotIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new RosterSnapshot([Row(11, 1), Row(22, 1)]));
        Assert.False(RosterSnapshot.TryFrom([Row(11, 1), Row(22, 1)], out RosterSnapshot? snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void MoreRowsThanSlotsIsRejected()
    {
        RosterEntry[] rows = Enumerable.Range(1, NetConfig.MAX_PLAYERS)
            .Select(i => Row(i, (byte)i))
            .Append(Row(99, 1))
            .ToArray();

        Assert.Throws<ArgumentException>(() => new RosterSnapshot(rows));
        Assert.False(RosterSnapshot.TryFrom(rows, out _));
    }

    [Fact]
    public void AFullTableIsAccepted()
    {
        RosterEntry[] rows = Enumerable.Range(1, NetConfig.MAX_PLAYERS)
            .Select(i => Row(i, (byte)i))
            .ToArray();

        Assert.True(RosterSnapshot.TryFrom(rows, out RosterSnapshot? snapshot));
        Assert.Equal(rows, snapshot!.Entries);
    }

    [Fact]
    public void TheEmptyTableHoldsNobody()
    {
        Assert.Empty(RosterSnapshot.EMPTY.Entries);
        Assert.Null(RosterSnapshot.EMPTY.PeerInSlot(new NetSlot(1)));
        Assert.False(RosterSnapshot.EMPTY.TryFind(1, out _));
    }
}
