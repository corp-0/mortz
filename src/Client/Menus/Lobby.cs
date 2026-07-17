using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Stats;

namespace Mortz.Client.Menus;

/// <summary>
/// Pre-match lobby: shows who is connected with their ping, session wins, and
/// ready state, plus a local ready toggle. The server starts the match once
/// everyone is ready.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class Lobby : Control
{
    [Signal] public delegate void ReadyToggledEventHandler(bool ready);

    [Export] private VBoxContainer _playerList = null!;
    [Export] private Button _readyButton = null!;

    private long[] _peerIds = [];
    private string[] _playerNames = [];
    private byte[] _readyFlags = [];
    private long _localId;
    private bool _localReady;
    private bool _subscribed;

    [Dependency]
    public IClientStats Stats => this.DependOn<IClientStats>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Stats.Changed += OnStatsChanged;
        _subscribed = true;
    }

    public void OnExitTree()
    {
        if (_subscribed)
            Stats.Changed -= OnStatsChanged;
        _subscribed = false;
    }

    public void ResetLocalReady()
    {
        _localReady = false;
        _readyButton.Text = "READY UP";
    }

    public void UpdatePlayers(long[] peerIds, string[] playerNames, byte[] readyFlags, long localId)
    {
        _peerIds = peerIds;
        _playerNames = playerNames;
        _readyFlags = readyFlags;
        _localId = localId;
        RenderPlayers();
    }

    public void OnReadyPressed()
    {
        _localReady = !_localReady;
        _readyButton.Text = _localReady ? "CANCEL READY" : "READY UP";
        EmitSignal(SignalName.ReadyToggled, _localReady);
    }

    private void OnStatsChanged()
    {
        if (Visible)
            RenderPlayers();
    }

    private void RenderPlayers()
    {
        foreach (Node child in _playerList.GetChildren())
            child.QueueFree();
        int count = Math.Min(_peerIds.Length, Math.Min(_playerNames.Length, _readyFlags.Length));
        for (int i = 0; i < count; i++)
        {
            string self = _peerIds[i] == _localId ? " (you)" : "";
            bool ready = _readyFlags[i] != 0;
            PanelContainer slot = new() { CustomMinimumSize = new Vector2(0, 44) };
            StyleBoxFlat background = new()
            {
                BgColor = new Color("111827"),
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomRight = 5,
                CornerRadiusBottomLeft = 5,
            };
            slot.AddThemeStyleboxOverride("panel", background);

            MarginContainer margin = new();
            margin.AddThemeConstantOverride("margin_left", 12);
            margin.AddThemeConstantOverride("margin_right", 12);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 14);
            margin.AddChild(row);
            slot.AddChild(margin);

            row.AddChild(new Label
            {
                Text = $"{_playerNames[i]}{self}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
            int wins = Stats.Wins(_peerIds[i]);
            row.AddChild(StatLabel(wins == 1 ? "1 WIN" : $"{wins} WINS", new Color("fbbf24"), 64));
            row.AddChild(StatLabel(
                Stats.PingMs(_peerIds[i]) is { } ping ? $"{ping} ms" : "... ms",
                new Color("64748b"), 64));
            row.AddChild(StatLabel(ready ? "READY" : "WAITING",
                ready ? new Color("86efac") : new Color("94a3b8"), 80));
            _playerList.AddChild(slot);
        }
    }

    private static Label StatLabel(string text, Color color, int minWidth) => new()
    {
        Text = text,
        Modulate = color,
        HorizontalAlignment = HorizontalAlignment.Right,
        CustomMinimumSize = new Vector2(minWidth, 0),
    };
}
