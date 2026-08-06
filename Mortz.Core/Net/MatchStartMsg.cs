namespace Mortz.Core.Net;

/// <summary>All lobby-transition players have loaded; prediction and the
/// authoritative match begin after this reliable signal.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct MatchStartMsg(int Generation);
