using Mortz.Core.Chat.Commands;

namespace Mortz.Client.Chat;

/// <summary>Base of client-executed chat commands. Subclass, decorate with
/// [ChatCommand], implement TryBind and Execute; registration is generated.</summary>
public abstract class ClientChatCommand : ChatCommand<ClientCommandContext>;
