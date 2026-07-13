namespace Mortz.Core.Net.Messages;

/// <summary>Dev/E2E helper: ask the server to carve a circle at a point.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct DebugCarveMsg(int X, int Y);
