using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Net;

namespace Mortz.Client.Spectating;

[Meta(typeof(IAutoNode))]
public partial class SpectatorController : Node
{
    [Export] private Camera2D _camera = null!;
    [Export] private SpectatorHud _hud = null!;

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private ClientMatchState MatchState => this.DependOn<ClientMatchState>();

    private readonly List<int> _targets = [];
    private MatchParticipation _participation = MatchParticipation.Active;
    private Vector2 _fallback;
    private int? _targetPeerId;
    private bool _replayActive;
    private bool _matchEnded;

    public override void _Notification(int what) => this.Notify(what);

    public void Initialize(Vector2 fallback)
    {
        _fallback = fallback;
        _camera.GlobalPosition = fallback;
    }

    public void OnResolved()
    {
        _participation = MatchState.Participation;
        _matchEnded = MatchState.Winner != null;
        MatchState.ParticipationChanged += OnParticipationChanged;
        MatchState.WinnerChanged += OnWinnerChanged;
        ApplyPresentation();
    }

    public void OnExitTree()
    {
        MatchState.ParticipationChanged -= OnParticipationChanged;
        MatchState.WinnerChanged -= OnWinnerChanged;
    }

    private void OnParticipationChanged(MatchParticipation next)
    {
        _participation = next;
        if (next.Activity != MatchActivity.SPECTATING)
            _targetPeerId = null;
        ApplyPresentation();
    }

    /// <summary>Once the match ends nobody respawns; the winner banner owns
    /// the screen and the respawn countdown would be a lie.</summary>
    private void OnWinnerChanged(Victor? winner)
    {
        if (winner == null)
            return;
        _matchEnded = true;
        _hud.HideStatus();
    }

    public void Present(IReadOnlyList<RenderPlayer> players, Vector2? localPosition,
        int newestTick)
    {
        if (_matchEnded)
            return;
        _targets.Clear();
        foreach (RenderPlayer player in players)
        {
            if (player.PeerId != Network.LocalPeerId && player.RespawnTicks == 0)
                _targets.Add(player.PeerId);
        }
        _targets.Sort();

        float seconds = SecondsUntilReturn(newestTick);
        switch (_participation.Activity)
        {
            case MatchActivity.ACTIVE:
                if (localPosition is Vector2 activePosition)
                    _camera.GlobalPosition = activePosition;
                _hud.HideStatus();
                break;
            case MatchActivity.DEATH_PRESENTATION:
                if (localPosition is Vector2 deathPosition)
                    _camera.GlobalPosition = deathPosition;
                _hud.ShowDeathPresentation(seconds);
                break;
            case MatchActivity.SPECTATING:
                PresentSpectator(players, seconds);
                break;
        }
    }

    public void SetReplayActive(bool active)
    {
        _replayActive = active;
        _camera.Enabled = !active;
        if (active)
            _hud.HideStatus();
        else
            ApplyPresentation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_replayActive || _matchEnded ||
            _participation.Activity != MatchActivity.SPECTATING || ChatInputGuard.IsTyping)
        {
            return;
        }
        if (@event.IsActionPressed("move_left"))
            Cycle(-1);
        else if (@event.IsActionPressed("move_right"))
            Cycle(1);
    }

    private void PresentSpectator(IReadOnlyList<RenderPlayer> players, float seconds)
    {
        if (_targetPeerId is not int target || !_targets.Contains(target))
            _targetPeerId = _targets.Count > 0 ? _targets[0] : null;
        if (_targetPeerId is not int selected)
        {
            _camera.GlobalPosition = _fallback;
            _hud.ShowSpectating("", seconds, canCycle: false);
            return;
        }
        foreach (RenderPlayer player in players)
        {
            if (player.PeerId != selected)
                continue;
            _camera.GlobalPosition = new Vector2(
                player.Position.X,
                player.Position.Y - SimConfig.PLAYER_HALF_HEIGHT);
            break;
        }
        _hud.ShowSpectating(Players.NameOf(selected), seconds, _targets.Count > 1);
    }

    private void Cycle(int direction)
    {
        if (_targets.Count < 2)
            return;
        int current = _targetPeerId is int peerId ? _targets.IndexOf(peerId) : -1;
        int next = (current + direction + _targets.Count) % _targets.Count;
        _targetPeerId = _targets[next];
    }

    private float SecondsUntilReturn(int newestTick)
    {
        if (_participation.ReturnTick < 0 || newestTick < 0)
            return 0;
        return Math.Max(0, _participation.ReturnTick - newestTick) / (float)SimConfig.TICK_RATE;
    }

    private void ApplyPresentation()
    {
        _camera.Enabled = !_replayActive;
        if (_replayActive || _participation.Activity == MatchActivity.ACTIVE)
            _hud.HideStatus();
    }
}
