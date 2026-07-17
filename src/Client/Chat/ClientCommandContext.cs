using Mortz.Client.Admin;

namespace Mortz.Client.Chat;

/// <summary>What client chat commands may touch. A command needing another
/// service widens this record, not the chat node's surface.</summary>
public readonly record struct ClientCommandContext(ClientChat Chat, ClientAdmin Admin);
