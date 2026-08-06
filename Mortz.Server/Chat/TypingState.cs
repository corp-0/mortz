namespace Mortz.Server.Chat;

/// <summary>One player's typing indicator.</summary>
public sealed class TypingState
{
    private bool _isTyping;

    /// <summary>True when the value flipped. Dropping no-op repeats is the flood guard.</summary>
    public bool Set(bool isTyping)
    {
        if (_isTyping == isTyping)
            return false;
        _isTyping = isTyping;
        return true;
    }
}
