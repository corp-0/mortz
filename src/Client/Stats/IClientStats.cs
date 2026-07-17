namespace Mortz.Client.Stats;

/// <summary>Scene-scoped read access to server-replicated per-player session
/// stats. Any UI or logic reads current values here instead of tracking the
/// wire messages itself.</summary>
public interface IClientStats
{
    event Action? Changed;

    /// <summary>Server-measured round trip in ms, or null before the first update.</summary>
    int? PingMs(long peerId);

    int Wins(long peerId);
}
