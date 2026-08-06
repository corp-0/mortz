using Mortz.Core.Match;
using Mortz.Core.Match.Teams;

namespace Mortz.Core.Net.Roster;

/// <summary>Zero on the wire means no team, and so does any byte the enum
/// does not name.</summary>
public static class TeamWire
{
    public const byte NONE = 0;

    public static byte ToByte(Team? team) => team is Team assigned ? (byte)assigned : NONE;

    public static Team? FromByte(byte value) => value switch
    {
        (byte)Team.BLUE => Team.BLUE,
        (byte)Team.RED => Team.RED,
        _ => null,
    };
}
