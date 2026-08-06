using Mortz.Core.Chat.Commands;
using Mortz.Core.Net.Chat;

namespace Mortz.Client.Chat.Commands;

[ChatCommand("roll", Usage = "/roll",
    Description = "Roll a number from 1 to 100 for everyone to see.")]
public sealed class RollChatCommand : ClientChatCommand
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
