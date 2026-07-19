using JetBrains.Annotations;
using Mortz.Core.Text;

namespace Mortz.Core.Chat;

public readonly record struct ChatEntry(
    ChatEntryKind Kind,
    [property: UsedImplicitly] long SenderId,
    string SenderName,
    string Text,
    ChatTextFormat TextFormat = ChatTextFormat.PLAIN)
{
    public RichText Render() => TextFormat switch
    {
        ChatTextFormat.MARKDOWN => ChatMarkdown.Render(Text),
        ChatTextFormat.RICH_TEXT => RichText.FromTrustedBbCode(Text),
        _ => new RichText(Text),
    };
}
