using Mortz.Core.Chat;
using Mortz.Core.Chat.Commands;

namespace Mortz.Client.Chat;

/// <summary>Scene-scoped API exposed by the persistent chat node.</summary>
public interface IClientChat
{
    ChatState State { get; }
    bool IsAdmin { get; }
    IEnumerable<ChatCommandMetadata> CommandCatalog { get; }
    event Action<bool>? AdminChanged;
    bool Submit(string? input);
    bool TrySignAdminAction(byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag);
}
