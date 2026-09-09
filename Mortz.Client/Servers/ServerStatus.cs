namespace Mortz.Client.Servers;

public enum ServerStatus
{
    UNKNOWN,
    PROBING,
    ONLINE,
    /// <summary>Answered, but running a build this client cannot join.</summary>
    INCOMPATIBLE,
    OFFLINE,
}
