using Mortz.Core.Chat.Commands;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Chat;

[ChatCommand("roll", Usage = "/roll",
    Description = "Roll a number from 1 to 100 for everyone to see.")]
internal sealed class RollChatCommand : ClientChatCommand
{
    public override bool TryBind(IReadOnlyList<string> arguments, out string error)
    {
        error = arguments.Count == 0 ? "" : "Usage: /roll";
        return arguments.Count == 0;
    }

    // The server owns the dice; the result comes back as a ROLL chat line.
    public override void Execute(ClientCommandContext context) =>
        new RollRequestMsg().SendToServer();
}
