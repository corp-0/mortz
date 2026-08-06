using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;

namespace Mortz.Core.Net.Match;

/// <summary>Someone is Remaining kills from winning, or no longer is. A state,
/// not an event: late joiners get the current one on sync. Kind is the
/// effective win condition, so the client can word the warning. LeaderId names
/// who: a peer id, or a team id when LeaderIsTeam. MatchProtocol is the only
/// encoder and decoder.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchPointMsg(
    bool Active,
    WinCondition Kind,
    byte Remaining,
    int LeaderId = 0,
    bool LeaderIsTeam = false
);
