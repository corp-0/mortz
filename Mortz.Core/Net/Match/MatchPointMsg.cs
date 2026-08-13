namespace Mortz.Core.Net.Match;

/// <summary>Someone is Remaining kills from winning, or no longer is. A state,
/// not an event: late joiners get the current one on sync. LeaderId names who:
/// a peer id, or a team id when LeaderIsTeam. MatchProtocol is the only encoder
/// and decoder.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchPointMsg(
    bool Active,
    byte Remaining,
    int LeaderId = 0,
    bool LeaderIsTeam = false
);
