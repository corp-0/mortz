namespace Mortz.Core.Net.Messages;

/// <summary>Signed admin request to replace the complete lobby ruleset.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct LobbyRulesUpdateMsg(
    byte[] Config,
    ulong Sequence,
    byte[] Tag);
