namespace Mortz.Core.Net.Messages;

/// <summary>Requests a one-use admin authentication challenge.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct AdminAuthRequestMsg();
