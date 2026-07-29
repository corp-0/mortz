using Mortz.Core.Chat.Commands;
using Mortz.Core.Text;

namespace Mortz.Client.Chat.Commands;

[ChatCommand("help", Usage = "/help", Description = "List chat commands.")]
internal sealed class HelpChatCommand : ClientChatCommand
{
    public override bool TryBind(IReadOnlyList<string> arguments, out string error)
    {
        error = arguments.Count == 0 ? "" : "Usage: /help";
        return arguments.Count == 0;
    }

    public override void Execute(ClientCommandContext context)
    {
        foreach (ChatCommandMetadata metadata in context.Chat.CommandCatalog)
        {
            RichText line = new RichText()
                .Add(metadata.Usage, new Style().Bold())
                .Add(" - ").Add(metadata.Description);
            context.Chat.AddPrivate(line);
        }
    }
}
