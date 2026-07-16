namespace Mortz.Core.Chat;

public enum ChatRejectReason
{
    None,
    Empty,
    TooLong,
    Command,
    RateLimited,
}
