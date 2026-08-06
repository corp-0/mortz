namespace Mortz.Core.Net.Admin;

/// <summary>HMAC proof of password knowledge for the current connection challenge.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct AdminProofMsg(byte[] Proof);
