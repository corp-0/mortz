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
        string adminPassword, string serverName, int skin, bool allowJoinInProgress);

    [Signal]
    public delegate void JoinRequestedEventHandler(string address, int port,
        string playerName, int skin);

    [Export] private MenuBackdrop _backdrop = null!;
    [Export] private Control _homePanel = null!;
    [Export] private ServerBrowser _browser = null!;
    [Export] private SettingsScreen _settingsScreen = null!;
    [Export] private Control _hostPanel = null!;
    [Export] private LineEdit _serverNameEdit = null!;
    [Export] private LineEdit _portEdit = null!;
    [Export] private LineEdit _adminPasswordEdit = null!;
    [Export] private CheckButton _allowJip = null!;
    [Export] private Label _status = null!;
    [Export] private AnimationPlayer _player = null!;

    [Dependency]
    private ClientSettings Settings => this.DependOn<ClientSettings>();

    private PendingAction _pendingAction;

    private enum PendingAction
    {
        NONE,
        JOIN,
        HOST,
    }

    public override void _Notification(int what) => this.Notify(what);

    public override void _Ready()
    {
        _portEdit.Text = NetConfig.DEFAULT_PORT.ToString();
        _browser.JoinRequested += OnBrowserJoinRequested;
        _browser.BackRequested += ShowHome;
        _settingsScreen.BackRequested += OnSettingsBackRequested;
        _settingsScreen.Saved += OnSettingsSaved;

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
        _settingsScreen.BackRequested -= OnSettingsBackRequested;
        _settingsScreen.Saved -= OnSettingsSaved;
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
        if (!HasIdentity(PendingAction.JOIN))
            return;
        OpenBrowser();
    }

    public void OnHostMenuPressed()
    {
        SetStatus("");
        if (!HasIdentity(PendingAction.HOST))
            return;
        OpenHostPanel();
    }

    public void OnSettingsPressed()
    {
        SetStatus("");
        _pendingAction = PendingAction.NONE;
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
            _adminPasswordEdit.Text, _serverNameEdit.Text.Trim(), Settings.Skin,
            _allowJip.ButtonPressed);
    }

    private void OnBrowserJoinRequested(string address, int port) =>
        EmitSignal(SignalName.JoinRequested, address, port, Settings.PlayerName, Settings.Skin);

    private bool HasIdentity(PendingAction action)
    {
        if (Settings.HasIdentity)
            return true;
        _pendingAction = action;
        ShowPanel(_settingsScreen);
        _settingsScreen.Open("Pick your name and skin before you play.");
        return false;
    }

    private void OnSettingsSaved()
    {
        PendingAction action = _pendingAction;
        _pendingAction = PendingAction.NONE;
        if (action == PendingAction.JOIN)
        {
            OpenBrowser();
            return;
        }
        if (action == PendingAction.HOST)
        {
            OpenHostPanel();
            return;
        }
        ShowHome();
    }

    private void OnSettingsBackRequested()
    {
        _pendingAction = PendingAction.NONE;
        ShowHome();
    }

    private void OpenBrowser()
    {
        ShowPanel(_browser);
        _browser.Open();
    }

    private void OpenHostPanel()
    {
        ShowPanel(_hostPanel);
        _serverNameEdit.GrabFocus();
    }
}
