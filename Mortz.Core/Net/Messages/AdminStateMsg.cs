namespace Mortz.Core.Net.Messages;

/// <summary>Private result of an admin authentication attempt.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.SERVER_TO_CLIENT)]
public readonly partial record struct AdminStateMsg(bool IsAdmin, string Status);
