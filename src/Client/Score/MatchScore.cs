using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Match;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Score;

namespace Mortz.Client.Score;

/// <summary>Applies the authoritative score stream onto players. The sync seed
/// replaces everything (it arrives with every match entry, so a new match or a
/// late join always starts from the server's truth); eliminations patch the
/// affected players afterwards.</summary>
[Meta(typeof(IAutoNode))]
public partial class MatchScore : Node,
    IHandle<ScoreSyncMsg>,
    IHandle<EliminationMsg>
{
    private SessionStateKey<ScoreState> _score;
    private TeamKills _teamKills;

    public event Action? Changed;

    public int Kills(int peerId) => Players.Find(peerId)?.State(_score).Kills ?? 0;
    public int Deaths(int peerId) => Players.Find(peerId)?.State(_score).Deaths ?? 0;
    public int TeamKills(Team team) => _teamKills[team];

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _score = Players.SessionKeys.Claim<ScoreState>();
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public void Handle(in ScoreSyncMsg message)
    {
        foreach (ClientPlayer player in Players)
        {
            ScoreState state = player.State(_score);
            state.Kills = 0;
            state.Deaths = 0;
        }
        foreach (ScoreRow row in message.Rows)
        {
            ScoreState state = Players.GetOrCreate(row.PeerId).State(_score);
            state.Kills = row.Kills;
            state.Deaths = row.Deaths;
        }
        _teamKills = new TeamKills(message.BlueKills, message.RedKills);
        Changed?.Invoke();
    }

    public void Handle(in EliminationMsg message)
    {
        ScoreState victim = Players.GetOrCreate(message.VictimId).State(_score);
        victim.Deaths = message.VictimDeaths;
        // On a suicide KillerKills carries the victim's own (possibly
        // penalized) count; otherwise it is the killer's total after the kill.
        if (message.Flags.HasFlag(EliminationFlags.SUICIDE))
            victim.Kills = message.KillerKills;
        else if (message.KillerId != 0)
            Players.GetOrCreate(message.KillerId).State(_score).Kills = message.KillerKills;
        if (message.RewardedId != 0)
            Players.GetOrCreate(message.RewardedId).State(_score).Kills = message.RewardedKills;
        _teamKills = new TeamKills(message.BlueKills, message.RedKills);
        Changed?.Invoke();
    }
}
