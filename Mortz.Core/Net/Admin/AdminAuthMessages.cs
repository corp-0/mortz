namespace Mortz.Core.Net.Admin;

[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct AdminAuthRequestMsg;

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct AdminChallengeMsg(byte[] Challenge);

/// <summary>HMAC proof for the current one-use challenge.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct AdminProofMsg(byte[] Proof);

[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct AdminStateMsg(bool IsAdmin, string Status);
