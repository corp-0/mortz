namespace Mortz.Core.Net.Chat;

/// <summary>Asks the server for a public dice roll; the result comes back to
/// everyone as a ROLL chat line.</summary>
[NetMessage(NetChannel.RELIABLE, NetDirection.CLIENT_TO_SERVER)]
public readonly partial record struct RollRequestMsg;
