using Mortz.Core.Net.Messages;

namespace Mortz.Client.Score;

/// <summary>Connected-session tables of the authoritative score stream. The
/// sync seed replaces everything (it arrives with every match entry, so a new
/// match or a late join always starts from the server's truth); eliminations
/// patch the affected rows afterwards.</summary>
public sealed class MatchScoreSession : IMatchScore, IDisposable
{
    private readonly Dictionary<long, int> _kills = new();
    private readonly Dictionary<long, int> _deaths = new();
    private int _team1Kills;
    private int _team2Kills;
    private bool _subscribed;

    public event Action? Changed;

    public int Kills(long peerId) => _kills.GetValueOrDefault(peerId);
    public int Deaths(long peerId) => _deaths.GetValueOrDefault(peerId);
    public int TeamKills(byte teamId) =>
        teamId switch { 1 => _team1Kills, 2 => _team2Kills, _ => 0 };

    public void Subscribe()
    {
        if (_subscribed)
            return;
        ScoreSyncMsg.Received += OnScoreSync;
        EliminationMsg.Received += OnElimination;
        _subscribed = true;
    }

    public void Clear()
    {
        _kills.Clear();
        _deaths.Clear();
        _team1Kills = 0;
        _team2Kills = 0;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            ScoreSyncMsg.Received -= OnScoreSync;
            EliminationMsg.Received -= OnElimination;
            _subscribed = false;
        }
        Clear();
    }

    private void OnScoreSync(ScoreSyncMsg message)
    {
        _kills.Clear();
        _deaths.Clear();
        int count = Math.Min(message.PeerIds.Length,
            Math.Min(message.Kills.Length, message.Deaths.Length));
        for (int i = 0; i < count; i++)
        {
            _kills[message.PeerIds[i]] = message.Kills[i];
            _deaths[message.PeerIds[i]] = message.Deaths[i];
        }
        _team1Kills = message.Team1Kills;
        _team2Kills = message.Team2Kills;
        Changed?.Invoke();
    }

    private void OnElimination(EliminationMsg message)
    {
        _deaths[message.VictimId] = message.VictimDeaths;
        // On a suicide KillerKills carries the victim's own (possibly
        // penalized) count; otherwise it is the killer's total after the kill.
        if ((message.Flags & EliminationFlags.SUICIDE) != 0)
            _kills[message.VictimId] = message.KillerKills;
        else if (message.KillerId != 0)
            _kills[message.KillerId] = message.KillerKills;
        _team1Kills = message.Team1Kills;
        _team2Kills = message.Team2Kills;
        Changed?.Invoke();
    }
}
