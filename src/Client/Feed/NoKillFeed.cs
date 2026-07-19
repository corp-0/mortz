namespace Mortz.Client.Feed;

/// <summary>Silent kill feed for screens that have no provider (the lobby).</summary>
public sealed class NoKillFeed : IKillFeed
{
    public static readonly NoKillFeed Instance = new();

    public event Action<string>? LineAdded { add { } remove { } }
}
