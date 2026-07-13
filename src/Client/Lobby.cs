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
        _readyButton.Text = "Ready up";
    }

    public void UpdatePlayers(long[] peerIds, string[] playerNames, byte[] readyFlags, long localId)
    {
        foreach (Node child in _playerList.GetChildren())
            child.QueueFree();
        for (int i = 0; i < peerIds.Length; i++)
        {
            string self = peerIds[i] == localId ? " (you)" : "";
            string state = readyFlags[i] != 0 ? "ready" : "not ready";
            _playerList.AddChild(new Label { Text = $"{playerNames[i]}{self} - {state}" });
        }
    }

    public void OnReadyPressed()
    {
        _localReady = !_localReady;
        _readyButton.Text = _localReady ? "Unready" : "Ready up";
        EmitSignal(SignalName.ReadyToggled, _localReady);
    }
}
