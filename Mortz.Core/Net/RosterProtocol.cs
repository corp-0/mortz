using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Core.Net;

public static class RosterProtocol
{
    static RosterProtocol()
    {
        RosterMsg.Received += OnRoster;
        LobbyStateMsg.Received += OnLobbyState;
    }

    public static event Action<RosterSnapshot>? MatchRosterReceived;
    public static event Action<LobbyRoster>? LobbyRosterReceived;

    public static void BroadcastMatchRoster(RosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Encode(snapshot).Broadcast();
    }

    public static void BroadcastLobbyRoster(LobbyRoster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        Encode(roster).Broadcast();
    }

    private static RosterMsg Encode(RosterSnapshot snapshot) => new(
        snapshot.Entries.Select(entry => entry.PeerId).ToArray(),
        snapshot.Entries.Select(entry => entry.Name).ToArray(),
        snapshot.Entries.Select(entry => entry.Skin).ToArray(),
        snapshot.Entries.Select(entry => TeamWire.ToByte(entry.Team)).ToArray(),
        snapshot.Entries.Select(entry => entry.Slot.Value).ToArray());

    private static LobbyStateMsg Encode(LobbyRoster roster) => new(
        roster.Members.Select(member => member.PeerId).ToArray(),
        roster.Members.Select(member => member.Name).ToArray(),
        roster.Members.Select(member => member.Ready ? (byte)1 : (byte)0).ToArray(),
        roster.Members.Select(member => TeamWire.ToByte(member.Team)).ToArray(),
        roster.Offers.Select(offer => offer.From).ToArray(),
        roster.Offers.Select(offer => offer.To).ToArray());

    private static void OnRoster(RosterMsg message)
    {
        int count = message.PeerIds.Length;
        if (message.Names.Length != count || message.Skins.Length != count ||
            message.Teams.Length != count || message.Slots.Length != count)
            return;
        RosterEntry[] entries = new RosterEntry[count];
        for (int i = 0; i < count; i++)
        {
            if (!TryEntry(message, i, out RosterEntry entry))
                return;
            entries[i] = entry;
        }
        if (!RosterSnapshot.TryFrom(entries, out RosterSnapshot? snapshot))
            return;
        MatchRosterReceived?.Invoke(snapshot);
    }

    private static bool TryEntry(RosterMsg message, int index, out RosterEntry entry)
    {
        entry = default;
        if (message.PeerIds[index] <= 0)
            return false;
        if (!NetSlot.TryFrom(message.Slots[index], out NetSlot slot))
            return false;
        entry = new RosterEntry(message.PeerIds[index], message.Names[index], message.Skins[index],
            TeamWire.FromByte(message.Teams[index]), slot);
        return true;
    }

    private static void OnLobbyState(LobbyStateMsg message)
    {
        int count = message.PeerIds.Length;
        if (message.Names.Length != count || message.ReadyFlags.Length != count ||
            message.Teams.Length != count || message.SwapTo.Length != message.SwapFrom.Length)
        {
            return;
        }

        LobbyMember[] members = new LobbyMember[count];
        for (int i = 0; i < count; i++)
        {
            if (message.PeerIds[i] <= 0)
                return;
            members[i] = new LobbyMember(message.PeerIds[i], message.Names[i],
                message.ReadyFlags[i] != 0, TeamWire.FromByte(message.Teams[i]));
        }
        SwapOffer[] offers = new SwapOffer[message.SwapFrom.Length];
        for (int i = 0; i < offers.Length; i++)
        {
            offers[i] = new SwapOffer(message.SwapFrom[i], message.SwapTo[i]);
        }
        LobbyRosterReceived?.Invoke(new LobbyRoster(members, offers));
    }
}
