namespace Mortz.Core.Net;

/// <summary>Snapshot decode asks this once per remote player, so keep it
/// allocation free.</summary>
public interface IPeerSlots
{
    int? PeerInSlot(NetSlot slot);
}
