namespace Mortz.Core.Net.Messages;

/// <summary>Parallel arrays: everyone in the pre-match lobby with their ready
/// state and lobby team (0 = none; assigned live while the ruleset says teams).</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct LobbyStateMsg(
    long[] PeerIds, string[] Names, byte[] ReadyFlags, byte[] Teams);
