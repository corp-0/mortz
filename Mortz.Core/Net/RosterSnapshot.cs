using System.Diagnostics.CodeAnalysis;

namespace Mortz.Core.Net;

/// <summary>The match roster as of one broadcast: each peer once, each slot once.</summary>
public sealed class RosterSnapshot : IPeerSlots
{
    public static readonly RosterSnapshot EMPTY = new([]);

    private readonly Dictionary<long, RosterEntry> _byPeer;
    private readonly Dictionary<byte, long> _bySlot;

    public RosterSnapshot(IReadOnlyList<RosterEntry> entries)
    {
        if (!Index(entries, out _byPeer, out _bySlot))
            throw new ArgumentException(
                "A roster holds each peer once, each slot once, and at most MAX_PLAYERS rows.",
                nameof(entries));
        Entries = [.. entries];
    }

    private RosterSnapshot(IReadOnlyList<RosterEntry> entries,
        Dictionary<long, RosterEntry> byPeer, Dictionary<byte, long> bySlot)
    {
        Entries = [.. entries];
        _byPeer = byPeer;
        _bySlot = bySlot;
    }

    public IReadOnlyList<RosterEntry> Entries { get; }

    public bool TryFind(long peerId, out RosterEntry entry) =>
        _byPeer.TryGetValue(peerId, out entry);

    public long? PeerInSlot(NetSlot slot) =>
        _bySlot.TryGetValue(slot.Value, out long peerId) ? peerId : null;

    /// <summary>Wire path: a bad table is dropped, not thrown.</summary>
    public static bool TryFrom(IReadOnlyList<RosterEntry> entries,
        [NotNullWhen(true)] out RosterSnapshot? snapshot)
    {
        snapshot = Index(entries, out Dictionary<long, RosterEntry> byPeer,
            out Dictionary<byte, long> bySlot)
            ? new RosterSnapshot(entries, byPeer, bySlot)
            : null;
        return snapshot != null;
    }

    private static bool Index(IReadOnlyList<RosterEntry> entries,
        out Dictionary<long, RosterEntry> byPeer, out Dictionary<byte, long> bySlot)
    {
        byPeer = new Dictionary<long, RosterEntry>(entries.Count);
        bySlot = new Dictionary<byte, long>(entries.Count);
        if (entries.Count > NetConfig.MAX_PLAYERS)
            return false;
        foreach (RosterEntry entry in entries)
        {
            if (!byPeer.TryAdd(entry.PeerId, entry))
                return false;
            if (!bySlot.TryAdd(entry.Slot.Value, entry.PeerId))
                return false;
        }
        return true;
    }
}
