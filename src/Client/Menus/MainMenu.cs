using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Menus.MainMenuAmbient;
using Mortz.Client.Settings;
using Mortz.Core.Net;

namespace Mortz.Client.Menus;

/// <summary>
/// Title menu: home, the server browser, hosting and settings. Pure UI;
/// ClientMain listens to the signals and runs the actual connection.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class MainMenu : Control
{
    [Signal]
    public delegate void HostRequestedEventHandler(int port, string playerName,
        string adminPassword, string serverName);

    [Signal]
    public delegate void JoinRequestedEventHandler(string address, int port,
        string playerName);

    [Export] private MenuBackdrop _backdrop = null!;
    [Export] private Control _homePanel = null!;
    [Export] private ServerBrowser _browser = null!;
    [Export] private SettingsScreen _settingsScreen = null!;
    [Export] private Control _hostPanel = null!;
    [Export] private LineEdit _serverNameEdit = null!;
    [Export] private LineEdit _portEdit = null!;
    [Export] private LineEdit _adminPasswordEdit = null!;
    [Export] private Label _status = null!;
    [Export] private AnimationPlayer _player = null!;

    [Dependency]
    private ClientSettings Settings => this.DependOn<ClientSettings>();

    public override void _Notification(int what) => this.Notify(what);

    public override void _Ready()
    {
        _portEdit.Text = NetConfig.DEFAULT_PORT.ToString();
        _browser.JoinRequested += OnBrowserJoinRequested;
        _browser.BackRequested += ShowHome;
        _settingsScreen.BackRequested += ShowHome;

        if (_backdrop.HasAnimatedBackground)
        {
            return;
        }

        _homePanel.Show();
        SetProcessInput(false);
    }

    public override void _ExitTree()
    {
        _browser.JoinRequested -= OnBrowserJoinRequested;
        _browser.BackRequested -= ShowHome;
        _settingsScreen.BackRequested -= ShowHome;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsPressed())
            StartIntro();
    }

    public void AutoStartIntro()
    {
        if (_backdrop.HasAnimatedBackground)
            StartIntro();
    }

    private void StartIntro()
    {
        _player.Play("start_sequence");
        _backdrop.StartSequence();
        SetProcessInput(false);
    }

    public void SetStatus(string text) => _status.Text = text;

    public void ShowHome() => ShowPanel(_homePanel);

    private void ShowPanel(Control panel)
    {
        _homePanel.Visible = panel == _homePanel;
        _browser.Visible = panel == _browser;
        _settingsScreen.Visible = panel == _settingsScreen;
        _hostPanel.Visible = panel == _hostPanel;
    }

    // ---- button handlers (connected in MainMenu.tscn) ----

    public void OnJoinMenuPressed()
    {
        SetStatus("");
        if (!HasPlayerName())
            return;
        ShowPanel(_browser);
        _browser.Open();
    }

    public void OnHostMenuPressed()
    {
        SetStatus("");
        if (!HasPlayerName())
            return;
        ShowPanel(_hostPanel);
        _serverNameEdit.GrabFocus();
    }

    public void OnSettingsPressed()
    {
        SetStatus("");
        ShowPanel(_settingsScreen);
        _settingsScreen.Open();
    }

    public void OnBackPressed()
    {
        SetStatus("");
        ShowPanel(_homePanel);
    }

    public void OnExitPressed() => GetTree().Quit();

    public void OnHostConfirmed()
    {
        if (!int.TryParse(_portEdit.Text.Trim(), out int port) || port is < 1 or > 65535)
        {
            SetStatus("Invalid port.");
            return;
        }
        EmitSignal(SignalName.HostRequested, port, Settings.PlayerName,
            _adminPasswordEdit.Text, _serverNameEdit.Text.Trim());
    }

    private void OnBrowserJoinRequested(string address, int port) =>
        EmitSignal(SignalName.JoinRequested, address, port, Settings.PlayerName);

    private bool HasPlayerName()
    {
        if (Settings.PlayerName.Length > 0)
            return true;
        ShowPanel(_settingsScreen);
        _settingsScreen.Open("Pick a name before you play.");
        return false;
    }
}
