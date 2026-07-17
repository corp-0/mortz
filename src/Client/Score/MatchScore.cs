namespace Mortz.Client.Score;

/// <summary>Persistent owner of the replicated match score and its connection
/// lifecycle. Provides <see cref="IMatchScore"/> to descendant scenes.</summary>
public partial class MatchScore : SessionScopedNode, IMatchScore
{
    private readonly MatchScoreSession _session = new();

    public event Action? Changed
    {
        add => _session.Changed += value;
        remove => _session.Changed -= value;
    }

    public int Kills(long peerId) => _session.Kills(peerId);
    public int Deaths(long peerId) => _session.Deaths(peerId);
    public int TeamKills(byte teamId) => _session.TeamKills(teamId);

    protected override void OnServiceReady() => _session.Subscribe();
    protected override void OnServiceExit() => _session.Dispose();
    protected override void OnSessionBoundary() => _session.Clear();
}
