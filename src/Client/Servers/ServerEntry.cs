using Mortz.Core.Net.Query;

namespace Mortz.Client.Servers;

/// <summary>One row of the browser: where it is, why it is listed, and what
/// the last probe said.</summary>
public sealed class ServerEntry(ServerEndpoint endpoint, ServerSource source, string label = "")
{
    public ServerEndpoint Endpoint { get; } = endpoint;
    public ServerSource Source { get; set; } = source;

    /// <summary>What the player called it. Shown until the server answers with
    /// a name of its own.</summary>
    public string Label { get; set; } = label;

    public ServerStatus Status { get; set; } = ServerStatus.UNKNOWN;
    public int PingMs { get; set; }
    public ServerInfo? Info { get; set; }

    public bool IsFavorite => Source is ServerSource.PINNED or ServerSource.FAVORITE;

    public bool CanToggleFavorite => Source != ServerSource.PINNED;

    /// <summary>Not the enum's numeric value, so reordering ServerSource
    /// cannot reorder the browser.</summary>
    public int SortRank => Source switch
    {
        ServerSource.PINNED => 0,
        ServerSource.FAVORITE => 1,
        ServerSource.LAN => 2,
        _ => 3,
    };

    public bool HasName => Info is { Name.Length: > 0 } || Label.Length > 0;

    public string DisplayName
    {
        get
        {
            if (Info is { Name.Length: > 0 } info)
                return info.Name;
            return Label.Length > 0 ? Label : Endpoint.ToString();
        }
    }
}
