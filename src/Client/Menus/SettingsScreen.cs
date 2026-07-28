using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Settings;

namespace Mortz.Client.Menus;

/// <summary>Set your name once. Thin on purpose, Steam will own identity and
/// most real options later.</summary>
[Meta(typeof(IAutoNode))]
public partial class SettingsScreen : Control
{
    [Signal] public delegate void BackRequestedEventHandler();

    [Export] private LineEdit _playerNameEdit = null!;
    [Export] private Label _status = null!;

    [Dependency]
    private ClientSettings Settings => this.DependOn<ClientSettings>();

    public override void _Notification(int what) => this.Notify(what);

    public void Open(string prompt = "")
    {
        _playerNameEdit.Text = Settings.PlayerName;
        _status.Text = prompt;
        _playerNameEdit.GrabFocus();
    }

    // ---- button handlers (connected in Settings.tscn) ----

    public void OnSavePressed()
    {
        Settings.SetPlayerName(_playerNameEdit.Text);
        _playerNameEdit.Text = Settings.PlayerName;
        if (Settings.PlayerName.Length == 0)
        {
            _status.Text = "Enter a name to play under.";
            return;
        }
        EmitSignal(SignalName.BackRequested);
    }

    public void OnBackPressed() => EmitSignal(SignalName.BackRequested);
}
