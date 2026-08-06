namespace Mortz.Core.Net.Match;

/// <summary>Signed admin request to end the match and send everyone back to the lobby.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct EndMatchRequestMsg(
    ulong Sequence,
    byte[] Tag);
