namespace Mortz.Core.Net;

/// <summary>Snapshot decode asks this once per remote player, so keep it
/// allocation free.</summary>
public interface IPeerSlots
{
    long? PeerInSlot(NetSlot slot);
}
