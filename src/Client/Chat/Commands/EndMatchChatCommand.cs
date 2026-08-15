using Mortz.Core.Chat.Commands;
using Mortz.Core.Net.Match;

namespace Mortz.Client.Chat.Commands;

[ChatCommand("endmatch", Usage = "/endmatch",
    Description = "End the current match and return everyone to the lobby (admin only).")]
public sealed class EndMatchChatCommand : ClientChatCommand
{
    public override bool TryBind(IReadOnlyList<string> arguments, out string error)
    {
        if (arguments.Count > 0)
        {
            error = "Usage: /endmatch";
            return false;
        }
        error = "";
        return true;
    }

    public override void Execute(ClientCommandContext context)
    {
        if (!context.Admin.TrySignAdminAction(EndMatchAction.ACTION,
                EndMatchAction.SignablePayload(),
                out ulong sequence, out byte[] tag))
        {
            context.Chat.AddPrivate(
                "Admin access required. Authenticate with /admin <password> in the lobby.");
            return;
        }
        new EndMatchRequestMsg(sequence, tag).SendToServer(context.Sender);
    }
}
