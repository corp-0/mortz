namespace Mortz.Core.Net.Messages;

/// <summary>The server ended one of the receiver's predicted shells early.
/// Sent reliably on deflection so retirement does not depend on observing the
/// short-lived authoritative shell in an unreliable snapshot.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct ShellRetireMsg(int SpawnSeq);
