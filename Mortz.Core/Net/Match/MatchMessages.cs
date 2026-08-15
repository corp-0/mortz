using Mortz.Core.Admin;
using Mortz.Core.Match.Events;
using Mortz.Core.Match.Participation;

namespace Mortz.Core.Net.Match;

/// <summary>Kill-feed entry with updated scores</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct EliminationMsg(
    int KillerId,
    int VictimId,
    EliminationFlags Flags,
    int KillerKills,
    int VictimDeaths,
    int RewardedId,
    int RewardedKills,
    int BlueKills,
    int RedKills);

[Flags]
public enum EliminationFlags : byte
{
    NONE = 0,
    SUICIDE = 1 << 0,
    FALL = 1 << 1,
    TEAM_KILL = 1 << 2,
    OWNED = 1 << 3,
    FIRST_BLOOD = 1 << 4,
}

/// <summary>Signed admin request to end the match and return to the lobby.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct EndMatchRequestMsg(
    ulong Sequence,
    byte[] Tag);

public static class EndMatchAction
{
    public const byte ACTION = AdminAction.END_MATCH;

    public static byte[] SignablePayload() => [];
}

/// <summary>Tick selects the render-history frame; Death and optional Impact
/// coordinates replay non-mortar deaths.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct FinalKillMsg(
    int Tick,
    int KillerId,
    int VictimId,
    FinalKillFlags Flags,
    short DeathX,
    short DeathY,
    short ImpactX,
    short ImpactY,
    byte BlastRadius);

[Flags]
public enum FinalKillFlags : byte
{
    NONE = 0,
    EXPLOSION = 1 << 0,
    FALL = 1 << 1,
    SUICIDE = 1 << 2,
    TEAM_KILL = 1 << 3,
    OWNED = 1 << 4,
}

/// <summary>Sent after its elimination; VictimId is 0 when unused, Magnitude is
/// the chain, streak, or body count, and Detail contains
/// <see cref="SuicideCause"/> for SUICIDE.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct GameEventMsg(
    GameEventKind Kind,
    int ActorId,
    int VictimId,
    byte Magnitude,
    byte Detail = 0
);

/// <summary>WinnerId is a team ID when ByTeam.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchEndMsg(bool ByTeam, int WinnerId);

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchParticipationMsg(
    MatchSeat Seat,
    MatchActivity Activity,
    SpectateReason Reason,
    int ReturnTick);

/// <summary>Current match-point state, including on late-join sync; LeaderId is
/// a team ID when LeaderIsTeam, otherwise a peer ID.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchPointMsg(
    bool Active,
    byte Remaining,
    int LeaderId = 0,
    bool LeaderIsTeam = false
);
