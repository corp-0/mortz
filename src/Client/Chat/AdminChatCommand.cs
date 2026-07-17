using Mortz.Core.Chat.Commands;

namespace Mortz.Client.Chat;

[ChatCommand("admin", Usage = "/admin <password>",
    Description = "Authenticate as a lobby admin.", Sensitive = true)]
internal sealed class AdminChatCommand : ClientChatCommand
{
    private string _password = "";

    public override bool TryBind(IReadOnlyList<string> arguments, out string error)
    {
        if (arguments is not [{ Length: > 0 } password])
        {
            error = "Usage: /admin <password>";
            return false;
        }
        _password = password;
        error = "";
        return true;
    }

    public override void Execute(ClientCommandContext context) =>
        context.Admin.BeginAuthentication(_password);
}
