namespace Mortz.Client.Stats;

/// <summary>Persistent owner of per-player session stats and their connection
/// lifecycle. Provides <see cref="IClientStats"/> to descendant scenes.</summary>
public partial class ClientStats : SessionScopedNode, IClientStats
{
    private readonly ClientStatsSession _session = new();

    public event Action? Changed
    {
        add => _session.Changed += value;
        remove => _session.Changed -= value;
    }

    public int? PingMs(long peerId) => _session.PingMs(peerId);
    public int Wins(long peerId) => _session.Wins(peerId);

    protected override void OnServiceReady() => _session.Subscribe();
    protected override void OnServiceExit() => _session.Dispose();
    protected override void OnSessionBoundary() => _session.Clear();
}
