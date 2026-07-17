namespace Mortz.Client.Roster;

/// <summary>Scene-scoped lookup into the replicated player roster.</summary>
public interface IClientRoster
{
    string NameOf(long peerId);
}
