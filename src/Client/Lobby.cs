using Godot;

namespace Mortz.Client;

/// <summary>
/// Pre-match lobby: shows who is connected and their ready state, with a
/// local ready toggle. The server starts the match once everyone is ready.
/// </summary>
public partial class Lobby : Control
{
    [Signal] public delegate void ReadyToggledEventHandler(bool ready);

    [Export] private VBoxContainer _playerList = null!;
    [Export] private Button _readyButton = null!;

    private bool _localReady;

    public void ResetLocalReady()
    {
        _localReady = false;
        _readyButton.Text = "READY UP";
    }

    public void UpdatePlayers(long[] peerIds, string[] playerNames, byte[] readyFlags, long localId)
    {
        foreach (Node child in _playerList.GetChildren())
            child.QueueFree();
        int count = Math.Min(peerIds.Length, Math.Min(playerNames.Length, readyFlags.Length));
        for (int i = 0; i < count; i++)
        {
            string self = peerIds[i] == localId ? " (you)" : "";
            bool ready = readyFlags[i] != 0;
            HBoxContainer row = new();
            row.AddChild(new Label
            {
                Text = $"{playerNames[i]}{self}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
            row.AddChild(new Label
            {
                Text = ready ? "READY" : "WAITING",
                Modulate = ready ? new Color("86efac") : new Color("94a3b8"),
            });
            _playerList.AddChild(row);
        }
    }

    public void OnReadyPressed()
    {
        _localReady = !_localReady;
        _readyButton.Text = _localReady ? "CANCEL READY" : "READY UP";
        EmitSignal(SignalName.ReadyToggled, _localReady);
    }
}
