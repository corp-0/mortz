namespace Mortz.Client;

/// <summary>Prevents chat keystrokes from also controlling the local player.</summary>
public static class ChatInputGuard
{
    private static readonly HashSet<object> _owners = new(ReferenceEqualityComparer.Instance);

    public static bool IsTyping => _owners.Count > 0;

    public static void SetTyping(object owner, bool isTyping)
    {
        if (isTyping)
            _owners.Add(owner);
        else
            _owners.Remove(owner);
    }
}
