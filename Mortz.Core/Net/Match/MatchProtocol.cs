using System.Diagnostics.CodeAnalysis;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Roster;

namespace Mortz.Core.Net.Match;

/// <summary>Turns match outcomes into the flat wire fields and back.</summary>
public static class MatchProtocol
{
    public static MatchEndMsg Encode(Victor victor) => victor switch
    {
        Victor.Player player => new MatchEndMsg(false, player.PeerId),
        Victor.Team team => new MatchEndMsg(true, TeamWire.ToByte(team.Value)),
        _ => throw new ArgumentOutOfRangeException(nameof(victor)),
    };

    public static MatchPointMsg Encode(MatchPoint? state)
    {
        if (state == null)
            return new MatchPointMsg(false, 0);
        byte remaining = (byte)Math.Clamp(state.Remaining, 1, byte.MaxValue);
        return state.Leader switch
        {
            Victor.Player player => new MatchPointMsg(true, remaining, player.PeerId),
            Victor.Team team => new MatchPointMsg(true, remaining,
                TeamWire.ToByte(team.Value), LeaderIsTeam: true),
            _ => new MatchPointMsg(true, remaining),
        };
    }

    public static bool TryDecode(
        MatchEndMsg message,
        [NotNullWhen(true)] out Victor? winner)
    {
        winner = DecodeVictor(message.ByTeam, message.WinnerId);
        return winner != null;
    }

    /// <summary>Null when match point lapsed; unrecognized leader just means no leader.</summary>
    public static MatchPoint? Decode(MatchPointMsg message)
    {
        if (!message.Active)
            return null;
        return new MatchPoint(
            Math.Max(1, (int)message.Remaining),
            DecodeVictor(message.LeaderIsTeam, message.LeaderId));
    }

    private static Victor? DecodeVictor(bool byTeam, int id)
    {
        if (!byTeam)
            return id > 0 ? new Victor.Player(id) : null;
        if (id is < 0 or > byte.MaxValue)
            return null;
        return TeamWire.FromByte((byte)id) is Team team ? new Victor.Team(team) : null;
    }
}
