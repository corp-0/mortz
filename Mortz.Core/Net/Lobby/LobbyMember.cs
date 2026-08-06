using Mortz.Core.Match;
using Mortz.Core.Match.Teams;

namespace Mortz.Core.Net.Lobby;

/// <summary>One seated player in the lobby broadcast; live state lives in the
/// server's lobby cells. Team is null when teams are off or not dealt yet.</summary>
[NetRow]
public readonly partial record struct LobbyMember
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
}
