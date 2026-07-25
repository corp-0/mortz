using Mortz.Core.Chat.Commands;

namespace Mortz.Client.Chat.Commands;

[ChatCommand("quit", Usage = "/quit", Description = "Disconnect from the server.")]
internal sealed class QuitChatCommand : ClientChatCommand
{
    public override bool TryBind(IReadOnlyList<string> arguments, out string error)
    {
        error = arguments.Count == 0 ? "" : "Usage: /quit";
        return arguments.Count == 0;
    }

    public override void Execute(ClientCommandContext context) =>
        context.SessionExit.LeaveSession("Left the server.");
}
