using Godot;
using Mortz.Core.Net.Messages;

namespace Mortz.Client;

/// <summary>
/// Screen-space score readout: the kill feed (top right, lines fade out), the
/// local kills/deaths counter (top left) and the winner banner shown during
/// the post-match victory lap. Fed by EliminationMsg/MatchEndMsg, names by
/// the roster. Dies with the GameView, which is what clears it (and resets
/// the counters) on the return to lobby.
/// </summary>
public partial class ScoreOverlay : Control
{
    private const int FEED_MAX_LINES = 6;
    private const float FEED_HOLD_TIME = 4f; // s before a line starts fading
    private const float FEED_FADE_TIME = 1f; // s

    [Export] private VBoxContainer _feed = null!;
    [Export] private Label _scoreLabel = null!;
    [Export] private Label _winnerLabel = null!;

    private readonly Dictionary<long, string> _names = new();
    private int _kills;
    private int _deaths;

    public override void _Ready()
    {
        RosterMsg.Received += OnRoster;
        EliminationMsg.Received += OnElimination;
        MatchEndMsg.Received += OnMatchEnd;
        UpdateScoreLabel();
    }

    public override void _ExitTree()
    {
        RosterMsg.Received -= OnRoster;
        EliminationMsg.Received -= OnElimination;
        MatchEndMsg.Received -= OnMatchEnd;
    }

    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        int count = Math.Min(msg.PeerIds.Length, msg.Names.Length);
        for (int i = 0; i < count; i++)
            _names[msg.PeerIds[i]] = msg.Names[i];
    }

    private void OnElimination(EliminationMsg msg)
    {
        long localId = Multiplayer.GetUniqueId();
        bool suicide = (msg.Flags & EliminationFlags.SUICIDE) != 0;
        if (!suicide && msg.KillerId == localId)
            _kills = msg.KillerKills;
        if (msg.VictimId == localId)
        {
            _deaths = msg.VictimDeaths;
            if (suicide)
                _kills = msg.KillerKills; // the suicide penalty may have moved it
        }
        UpdateScoreLabel();

        AddFeedLine(FormatFeedLine(msg, Name));
    }

    public static string FormatFeedLine(EliminationMsg msg, Func<long, string> name) =>
        (msg.Flags & EliminationFlags.OWNED) != 0
            ? $"{name(msg.KillerId)} OWNED {name(msg.VictimId)}"
            : (msg.Flags & EliminationFlags.FALL) != 0
                ? $"{name(msg.VictimId)} fell out of the world"
                : (msg.Flags & EliminationFlags.SUICIDE) != 0
                    ? $"{name(msg.VictimId)} blew themselves up"
                    : (msg.Flags & EliminationFlags.TEAM_KILL) != 0
                        ? $"{name(msg.KillerId)} team-killed {name(msg.VictimId)}"
                        : $"{name(msg.KillerId)} killed {name(msg.VictimId)}";

    private void OnMatchEnd(MatchEndMsg msg)
    {
        _winnerLabel.Text = msg.ByTeam ? $"Team {msg.WinnerId} wins!" : $"{Name(msg.WinnerId)} wins!";
        _winnerLabel.Visible = true;
    }

    private void AddFeedLine(string text)
    {
        Label line = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Right };
        _feed.AddChild(line);
        if (_feed.GetChildCount() > FEED_MAX_LINES)
            _feed.GetChild(0).QueueFree();
        Tween tween = line.CreateTween();
        tween.TweenInterval(FEED_HOLD_TIME);
        tween.TweenProperty(line, "modulate:a", 0f, FEED_FADE_TIME);
        tween.TweenCallback(Callable.From(line.QueueFree));
    }

    private void UpdateScoreLabel() => _scoreLabel.Text = $"K {_kills} / D {_deaths}";

    private new string Name(long peerId) =>
        _names.TryGetValue(peerId, out string? name) ? name : $"Player {peerId}";
}
