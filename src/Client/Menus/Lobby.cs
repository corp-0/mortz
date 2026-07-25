using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Chat;

namespace Mortz.Client.Menus;

/// <summary>
/// Pre-match lobby shell: hosts the roster, settings, and chat surfaces plus
/// the local ready toggle.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class Lobby : Control, IProvide<ClientChat>
{
    [Signal] public delegate void ReadyToggledEventHandler(bool ready);

    [Export] private Button _readyButton = null!;
    [Export] private ClientChat _chat = null!;

    private bool _localReady;

    ClientChat IProvide<ClientChat>.Value() => _chat;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => this.Provide();

    public void OnReadyPressed()
    {
        _localReady = !_localReady;
        _readyButton.Text = _localReady ? "CANCEL READY" : "READY UP";
        EmitSignal(SignalName.ReadyToggled, _localReady);
    }
}
