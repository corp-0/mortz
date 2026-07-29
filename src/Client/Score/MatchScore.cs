using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;

namespace Mortz.Client.Score;

/// <summary>Connected-session tables of the authoritative score stream. The
/// sync seed replaces everything (it arrives with every match entry, so a new
/// match or a late join always starts from the server's truth); eliminations
/// patch the affected rows afterwards.</summary>
public partial class MatchScore : Node
{
    private readonly Dictionary<long, int> _kills = [];
    private readonly Dictionary<long, int> _deaths = [];
    private TeamKills _teamKills;

    public event Action? Changed;

    public int Kills(long peerId) => _kills.GetValueOrDefault(peerId);
    public int Deaths(long peerId) => _deaths.GetValueOrDefault(peerId);
    public int TeamKills(Team team) => _teamKills[team];

    public override void _Ready()
    {
        ScoreSyncMsg.Received += OnScoreSync;
        EliminationMsg.Received += OnElimination;
    }

    public override void _ExitTree()
    {
        ScoreSyncMsg.Received -= OnScoreSync;
        EliminationMsg.Received -= OnElimination;
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
        _teamKills = new TeamKills(message.BlueKills, message.RedKills);
        Changed?.Invoke();
    }

    private void OnElimination(EliminationMsg message)
    {
        _deaths[message.VictimId] = message.VictimDeaths;
        // On a suicide KillerKills carries the victim's own (possibly
        // penalized) count; otherwise it is the killer's total after the kill.
        if (message.Flags.HasFlag(EliminationFlags.SUICIDE))
            _kills[message.VictimId] = message.KillerKills;
        else if (message.KillerId != 0)
            _kills[message.KillerId] = message.KillerKills;
        if (message.RewardedId != 0)
            _kills[message.RewardedId] = message.RewardedKills;
        _teamKills = new TeamKills(message.BlueKills, message.RedKills);
        Changed?.Invoke();
    }
}
