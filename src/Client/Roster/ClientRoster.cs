namespace Mortz.Client.Roster;

/// <summary>Persistent owner of the replicated roster and its connection
/// lifecycle. Provides <see cref="IClientRoster"/> to descendant scenes.</summary>
public partial class ClientRoster : SessionScopedNode, IClientRoster
{
    private readonly ClientRosterSession _session = new();

    public string NameOf(long peerId) => _session.NameOf(peerId);

    protected override void OnServiceReady() => _session.Subscribe();
    protected override void OnServiceExit() => _session.Dispose();
    protected override void OnSessionBoundary() => _session.Clear();
}
