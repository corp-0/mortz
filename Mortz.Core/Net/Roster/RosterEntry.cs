using Mortz.Core.Match;
using Mortz.Core.Match.Teams;

namespace Mortz.Core.Net.Roster;

/// <summary>Slot must be in NetSlot's range; the constructor throws otherwise,
/// which drops a malformed message at decode.</summary>
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
