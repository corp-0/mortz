namespace Mortz.Client.Score;

/// <summary>Scene-scoped read access to the authoritative match score stream:
/// per-player tallies and team totals, seeded by the match sync and kept
/// current by eliminations. UI reads current values here instead of tracking
/// the wire messages itself.</summary>
public interface IMatchScore
{
    event Action? Changed;

    int Kills(long peerId);
    int Deaths(long peerId);
    int TeamKills(byte teamId);
}
