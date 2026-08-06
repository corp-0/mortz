namespace Mortz.Core.Net.Match;

/// <summary>The match is decided. WinnerId is a team id when ByTeam;
/// MatchProtocol is the only encoder and decoder.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchEndMsg(bool ByTeam, int WinnerId);
