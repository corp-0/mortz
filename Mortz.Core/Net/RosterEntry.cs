using Mortz.Core.Match;

namespace Mortz.Core.Net;

public readonly record struct RosterEntry
{
    public RosterEntry(int peerId, string name, byte skin, Team? team, NetSlot slot)
    {
        if (peerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(peerId));
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
    public NetSlot Slot { get; }
}
