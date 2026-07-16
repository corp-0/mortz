using Mortz.Core.Chat.Commands;

namespace Mortz.Client.Chat;

[ChatCommand("help", Usage = "/help", Description = "List chat commands.")]
internal sealed class HelpChatCommand : ClientChatCommand
{
    public override bool TryBind(IReadOnlyList<string> arguments, out string error)
    {
        error = arguments.Count == 0 ? "" : "Usage: /help";
        return arguments.Count == 0;
    }

    public override void Execute(ClientChatSession session) => session.ShowCommandHelp();
}
