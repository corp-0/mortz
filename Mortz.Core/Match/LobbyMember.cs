namespace Mortz.Core.Match;

/// <summary>One player seated in the pre-match lobby. Team is null when teams
/// are off or the seat is not dealt yet.</summary>
public readonly record struct LobbyMember
{
    public LobbyMember(int peerId, string name, bool ready, Team? team)
    {
        if (peerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(peerId));
        PeerId = peerId;
        Name = name;
        Ready = ready;
        Team = team;
    }

    public int PeerId { get; }
    public string Name { get; }
    public bool Ready { get; }
    public Team? Team { get; }

    public LobbyMember WithReady(bool ready) => new(PeerId, Name, ready, Team);
    public LobbyMember OnTeam(Team? team) => new(PeerId, Name, Ready, team);
}
