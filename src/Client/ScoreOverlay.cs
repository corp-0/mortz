using Godot;
using Mortz.Core.Net.Messages;

namespace Mortz.Client;

/// <summary>
/// Screen-space score readout: the kill feed (top right, lines fade out), the
/// local kills/deaths counter (top left) and the winner banner shown during
/// the post-match victory lap. Fed entirely by ScoreMsg/MatchEndMsg, names by
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
        ScoreMsg.Received += OnScore;
        MatchEndMsg.Received += OnMatchEnd;
        UpdateScoreLabel();
    }

    public override void _ExitTree()
    {
        RosterMsg.Received -= OnRoster;
        ScoreMsg.Received -= OnScore;
        MatchEndMsg.Received -= OnMatchEnd;
    }

    private void OnRoster(RosterMsg msg)
    {
        _names.Clear();
        for (int i = 0; i < msg.PeerIds.Length; i++)
            _names[msg.PeerIds[i]] = msg.Names[i];
    }

    private void OnScore(ScoreMsg msg)
    {
        long localId = Multiplayer.GetUniqueId();
        bool suicide = msg.KillerId == 0 || msg.KillerId == msg.VictimId;
        if (!suicide && msg.KillerId == localId)
            _kills = msg.KillerKills;
        if (msg.VictimId == localId)
        {
            _deaths = msg.VictimDeaths;
            if (suicide)
                _kills = msg.KillerKills; // the suicide penalty may have moved it
        }
        UpdateScoreLabel();

        AddFeedLine(msg.KillerId == 0
            ? $"{Name(msg.VictimId)} fell out of the world"
            : suicide
                ? $"{Name(msg.VictimId)} blew themselves up"
                : $"{Name(msg.KillerId)} killed {Name(msg.VictimId)}");
    }

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
