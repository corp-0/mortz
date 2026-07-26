using Mortz.Core.Sim;

namespace Mortz.Core.Match;

/// <summary>Enemies each player has killed since their own last death;
/// covering the whole enemy roster is a team wipe. Progress goes stale when
/// kills sit more than a window apart, so a wipe is a sustained run, not an
/// all-match tally.</summary>
public sealed class TeamWipeTracker
{
    private const float WINDOW_SECONDS = 15f;
    public const int WINDOW_TICKS = (int)(WINDOW_SECONDS * SimConfig.TICK_RATE);

    private readonly Dictionary<int, (HashSet<int> Killed, int Tick)> _killedSinceDeath = new();

    public void OnDeath(int victimId) => _killedSinceDeath.Remove(victimId);

    /// <summary>Records the kill and returns true when the killer has now
    /// killed every current member of the enemy team, which resets the
    /// set.</summary>
    public bool OnKill(int killerId, int victimId, int tick,
        IReadOnlyDictionary<int, byte> teams)
    {
        if (!teams.TryGetValue(killerId, out byte killerTeam) ||
            !teams.TryGetValue(victimId, out byte victimTeam) ||
            killerTeam == 0 || killerTeam == victimTeam)
            return false;
        if (!_killedSinceDeath.TryGetValue(killerId, out (HashSet<int> Killed, int Tick) state))
            state = (new HashSet<int>(), tick);
        else if (tick - state.Tick > WINDOW_TICKS)
            state.Killed.Clear();
        state.Killed.Add(victimId);
        _killedSinceDeath[killerId] = (state.Killed, tick);
        bool wipe = teams.All(pair =>
            pair.Value == killerTeam || pair.Value == 0 || state.Killed.Contains(pair.Key));
        if (wipe)
            state.Killed.Clear();
        return wipe;
    }

    public void RemovePlayer(int peerId)
    {
        _killedSinceDeath.Remove(peerId);
        foreach ((HashSet<int> killed, _) in _killedSinceDeath.Values)
        {
            killed.Remove(peerId);
        }
    }
}
