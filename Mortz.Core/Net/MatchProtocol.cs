using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Core.Net;

/// <summary>Turns match outcomes into the flat wire fields and back.</summary>
public static class MatchProtocol
{
    static MatchProtocol()
    {
        MatchEndMsg.Received += OnMatchEnd;
        MatchPointMsg.Received += OnMatchPoint;
    }

    public static event Action<Victor>? MatchEnded;

    /// <summary>Null when match point lapsed.</summary>
    public static event Action<MatchPoint?>? MatchPointChanged;

    public static void BroadcastMatchEnd(Victor winner) => EncodeMatchEnd(winner).Broadcast();

    public static void SendMatchEndTo(int peerId, Victor winner) =>
        EncodeMatchEnd(winner).SendTo(peerId);

    public static void BroadcastMatchPoint(WinCondition kind, MatchPoint? state) =>
        EncodeMatchPoint(kind, state).Broadcast();

    public static void SendMatchPointTo(int peerId, WinCondition kind, MatchPoint state) =>
        EncodeMatchPoint(kind, state).SendTo(peerId);

    private static MatchEndMsg EncodeMatchEnd(Victor victor) => victor switch
    {
        PlayerVictor player => new MatchEndMsg(false, player.PeerId),
        TeamVictor team => new MatchEndMsg(true, TeamWire.ToByte(team.Team)),
        _ => throw new ArgumentOutOfRangeException(nameof(victor)),
    };

    private static MatchPointMsg EncodeMatchPoint(WinCondition kind, MatchPoint? state)
    {
        if (state == null)
            return new MatchPointMsg(false, kind, 0);
        byte remaining = (byte)Math.Clamp(state.Remaining, 1, byte.MaxValue);
        return state.Leader switch
        {
            PlayerVictor player => new MatchPointMsg(true, kind, remaining, player.PeerId),
            TeamVictor team => new MatchPointMsg(true, kind, remaining,
                TeamWire.ToByte(team.Team), LeaderIsTeam: true),
            _ => new MatchPointMsg(true, kind, remaining),
        };
    }

    private static void OnMatchEnd(MatchEndMsg message)
    {
        if (DecodeVictor(message.ByTeam, message.WinnerId) is Victor winner)
            MatchEnded?.Invoke(winner);
    }

    /// <summary>A leader nobody can name degrades to no leader; dropping the
    /// whole transition would cost the banner too.</summary>
    private static void OnMatchPoint(MatchPointMsg message)
    {
        if (!message.Active)
        {
            MatchPointChanged?.Invoke(null);
            return;
        }
        MatchPointChanged?.Invoke(new MatchPoint(
            Math.Max(1, (int)message.Remaining),
            DecodeVictor(message.LeaderIsTeam, message.LeaderId)));
    }

    private static Victor? DecodeVictor(bool byTeam, int id)
    {
        if (!byTeam)
            return id > 0 ? new PlayerVictor(id) : null;
        if (id is < 0 or > byte.MaxValue)
            return null;
        return TeamWire.FromByte((byte)id) is Team team ? new TeamVictor(team) : null;
    }
}
