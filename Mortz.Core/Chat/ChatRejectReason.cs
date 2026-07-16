namespace Mortz.Core.Chat;

public enum ChatRejectReason
{
    NONE,
    EMPTY,
    TOO_LONG,
    COMMAND,
    RATE_LIMITED,
}
