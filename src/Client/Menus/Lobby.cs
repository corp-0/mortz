using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client.Menus;

/// <summary>
/// Pre-match lobby shell: hosts the roster, settings, and chat surfaces plus
/// the local ready toggle. Re-provides the services its subtree consumes, so
/// the mount point decides which instances the lobby sees.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class Lobby : Control,
    IProvide<MatchSetup>,
    IProvide<ClientStats>,
    IProvide<ClientAdmin>
{
    [Signal] public delegate void ReadyToggledEventHandler(bool ready);

    [Export] private Button _readyButton = null!;

    private bool _localReady;

    [Dependency]
    private MatchSetup Setup => this.DependOn<MatchSetup>();

    [Dependency]
    private ClientStats Stats => this.DependOn<ClientStats>();

    [Dependency]
    private ClientAdmin Admin => this.DependOn<ClientAdmin>();

    MatchSetup IProvide<MatchSetup>.Value() => Setup;
    ClientStats IProvide<ClientStats>.Value() => Stats;
    ClientAdmin IProvide<ClientAdmin>.Value() => Admin;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved() => this.Provide();

    public void OnReadyPressed()
    {
        _localReady = !_localReady;
        _readyButton.Text = _localReady ? "CANCEL READY" : "READY UP";
        EmitSignal(SignalName.ReadyToggled, _localReady);
    }
}
