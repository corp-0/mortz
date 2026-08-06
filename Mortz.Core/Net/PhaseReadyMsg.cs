namespace Mortz.Core.Net;

/// <summary>The screen for this phase is resolved and all of its handlers are registered.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct PhaseReadyMsg(int Generation);
