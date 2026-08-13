using Mortz.Core.Match.Teams;

namespace Mortz.Core.Net.Roster;

/// <summary>Throws for invalid peer IDs or slots, dropping malformed messages during decode.</summary>
[NetRow]
public readonly partial record struct RosterEntry
{
    public RosterEntry(int peerId, string name, byte skin, Team? team, byte slot)
    {
        if (peerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(peerId));
        if (!NetSlot.TryFrom(slot, out _))
            throw new ArgumentOutOfRangeException(nameof(slot));
        PeerId = peerId;
        Name = name;
        Skin = skin;
        Team = team;
        Slot = slot;
    }

    public int PeerId { get; }
    public string Name { get; }
    public byte Skin { get; }
    public Team? Team { get; }
    public byte Slot { get; }
}

/// <summary>Sent at match start and after each in-game join or leave.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct RosterMsg(RosterEntry[] Entries);

[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct TeamJoinRequestMsg(byte Team);

/// <summary>Repeating an offer cancels it; a matching offer from the target completes the swap.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct TeamSwapRequestMsg(int TargetPeerId);
