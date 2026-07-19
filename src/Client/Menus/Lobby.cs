using Godot;

namespace Mortz.Client.Menus;

/// <summary>
/// Pre-match lobby shell: hosts the roster, settings, and chat surfaces plus
/// the local ready toggle. Player presentation lives in the roster variants;
/// the server starts the match once everyone is ready.
/// </summary>
public partial class Lobby : Control
{
    [Signal] public delegate void ReadyToggledEventHandler(bool ready);

    [Export] private Button _readyButton = null!;

    private bool _localReady;

    public void OnReadyPressed()
    {
        _localReady = !_localReady;
        _readyButton.Text = _localReady ? "CANCEL READY" : "READY UP";
        EmitSignal(SignalName.ReadyToggled, _localReady);
    }
}
