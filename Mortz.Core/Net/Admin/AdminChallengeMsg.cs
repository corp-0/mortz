namespace Mortz.Core.Net.Admin;

/// <summary>Server-created session id and one-use nonce for admin authentication.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct AdminChallengeMsg(byte[] Challenge);
