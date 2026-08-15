using Mortz.Client.Admin;
using Mortz.Client.Session;
using Mortz.Core.Net;

namespace Mortz.Client.Chat.Commands;

/// <summary>What client chat commands may touch. A command needing another
/// service widens this record, not the chat node's surface.</summary>
public readonly record struct ClientCommandContext(
    ClientChat Chat, ClientAdmin Admin, ISessionExit SessionExit, IClientSender Sender);
