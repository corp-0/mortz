using Mortz.Core.Match.Teams;

namespace Mortz.Core.Net.Lobby;

/// <summary>One player in the server's lobby-state snapshot.</summary>
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

[NetRow]
public readonly partial record struct SwapOffer(int From, int To);

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbyStateMsg(
    LobbyMember[] Members,
    SwapOffer[] Offers);

[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct SetReadyMsg(bool Ready);
