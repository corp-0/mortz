namespace Mortz.Core.Chat.Commands;

public readonly record struct ChatCommandMetadata(
    ChatCommandName Name,
    string Usage,
    string Description,
    IReadOnlyList<ChatCommandName> Aliases,
    bool Sensitive = false);
