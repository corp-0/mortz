namespace Mortz.Core.Net;

/// <summary>Starts a lobby load. Match loads are started by WelcomeMsg, which
/// carries the same generation.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct PhaseLoadMsg(int Generation);
