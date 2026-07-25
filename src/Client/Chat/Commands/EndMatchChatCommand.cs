using Mortz.Core.Admin;
using Mortz.Core.Chat.Commands;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Chat.Commands;

[ChatCommand("endmatch", Usage = "/endmatch",
    Description = "End the current match and return everyone to the lobby (admin only).")]
internal sealed class EndMatchChatCommand : ClientChatCommand
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
        if (!context.Admin.TrySignAdminAction(AdminAction.END_MATCH, [],
                out ulong sequence, out byte[] tag))
        {
            context.Chat.State.AddSystem(
                "Admin access required. Authenticate with /admin <password> in the lobby.",
                isPrivate: true);
            return;
        }
        new EndMatchRequestMsg(sequence, tag).SendToServer();
    }
}
