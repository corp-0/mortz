using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Score;

namespace Mortz.Client.Match;

/// <summary>Decodes the reliable match-state stream for one mounted match.</summary>
[Meta(typeof(IAutoNode))]
public partial class ClientMatchStateAdapter : Node,
    IHandle<MatchParticipationMsg>,
    IHandle<MatchPointMsg>,
    IHandle<MatchEndMsg>,
    IHandle<ScoreSyncMsg>,
    IHandle<EliminationMsg>
{
    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private ClientMatchState _state = null!;
    private NetRouter? _routed;

    public void Initialize(ClientMatchState state) => _state = state;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
        _state.Close();
    }

    public void Handle(in MatchParticipationMsg message) =>
        _state.TryApplyParticipation(_state.Generation, new MatchParticipation(
            message.Seat, message.Activity, message.Reason, message.ReturnTick));

    public void Handle(in MatchPointMsg message) =>
        _state.TryApplyMatchPoint(_state.Generation, MatchProtocol.Decode(message));

    public void Handle(in MatchEndMsg message)
    {
        if (MatchProtocol.TryDecode(message, out Victor? winner))
            _state.TryApplyWinner(_state.Generation, winner);
    }

    public void Handle(in ScoreSyncMsg message) =>
        _state.TryReplaceScores(
            _state.Generation,
            message.Rows.Select(row => new MatchScoreRow(row.PeerId, row.Kills, row.Deaths))
                .ToArray(),
            new TeamKills(message.BlueKills, message.RedKills));

    public void Handle(in EliminationMsg message) =>
        _state.TryPatchScores(_state.Generation, new MatchScorePatch(
            message.KillerId,
            message.VictimId,
            message.Flags.HasFlag(EliminationFlags.SUICIDE),
            message.KillerKills,
            message.VictimDeaths,
            message.RewardedId,
            message.RewardedKills,
            new TeamKills(message.BlueKills, message.RedKills)));
}
