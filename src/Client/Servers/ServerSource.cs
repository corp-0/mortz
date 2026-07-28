namespace Mortz.Client.Servers;

/// <summary>Why an entry is in the list. Drives ordering, whether the star is
/// interactive, and whether the entry survives a restart.</summary>
public enum ServerSource
{
    /// <summary>The shipped playtest server, always present.</summary>
    PINNED,
    FAVORITE,
    LAN,
    DIRECT,
}
