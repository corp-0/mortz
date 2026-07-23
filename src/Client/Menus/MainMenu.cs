using Godot;
using Mortz.Client.Menus.MainMenuAmbient;
using Mortz.Core.Net;

namespace Mortz.Client.Menus;

/// <summary>
/// Title menu: pick Join or Host, then fill in the matching form. Pure UI;
/// ClientMain listens to the signals and runs the actual connection.
/// </summary>
public partial class MainMenu : Control
{
    [Signal] public delegate void HostRequestedEventHandler(int port, string playerName, string adminPassword);
    [Signal] public delegate void JoinRequestedEventHandler(string address, int port, string playerName);

    [Export] private MenuBackdrop _backdrop = null!;
    [Export] private Control _homePanel = null!;
    [Export] private Control _joinPanel = null!;
    [Export] private Control _hostPanel = null!;
    [Export] private LineEdit _joinNameEdit = null!;
    [Export] private LineEdit _addressEdit = null!;
    [Export] private LineEdit _hostNameEdit = null!;
    [Export] private LineEdit _portEdit = null!;
    [Export] private LineEdit _adminPasswordEdit = null!;
    [Export] private Label _status = null!;
    [Export] private AnimationPlayer _player = null!;

    public override void _Ready()
    {
        _portEdit.Text = NetConfig.DEFAULT_PORT.ToString();

        if (_backdrop.HasAnimatedBackground)
        {
            return;
        }

        _homePanel.Show();
        SetProcessInput(false);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsPressed())
        {
            _player.Play("start_sequence");
            _backdrop.StartSequence();
            SetProcessInput(false);
        }
    }

    public void SetStatus(string text) => _status.Text = text;

    public void ShowHome() => ShowPanel(_homePanel);

    private void ShowPanel(Control panel)
    {
        _homePanel.Visible = panel == _homePanel;
        _joinPanel.Visible = panel == _joinPanel;
        _hostPanel.Visible = panel == _hostPanel;
    }

    // ---- button handlers (connected in MainMenu.tscn) ----

    public void OnJoinMenuPressed()
    {
        SetStatus("");
        ShowPanel(_joinPanel);
        _joinNameEdit.GrabFocus();
    }

    public void OnHostMenuPressed()
    {
        SetStatus("");
        ShowPanel(_hostPanel);
        _hostNameEdit.GrabFocus();
    }

    public void OnBackPressed()
    {
        SetStatus("");
        ShowPanel(_homePanel);
    }

    public void OnJoinConfirmed()
    {
        string playerName = _joinNameEdit.Text.Trim();
        if (playerName.Length == 0)
        {
            SetStatus("Enter a player name.");
            return;
        }
        string address = _addressEdit.Text.Trim();
        int port = NetConfig.DEFAULT_PORT;
        int colon = address.LastIndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(address[(colon + 1)..], out port))
            {
                SetStatus("Invalid port.");
                return;
            }
            address = address[..colon];
        }
        if (address.Length == 0)
        {
            SetStatus("Enter a server address.");
            return;
        }
        EmitSignal(SignalName.JoinRequested, address, port, playerName);
    }

    public void OnHostConfirmed()
    {
        string playerName = _hostNameEdit.Text.Trim();
        if (playerName.Length == 0)
        {
            SetStatus("Enter a player name.");
            return;
        }
        if (!int.TryParse(_portEdit.Text.Trim(), out int port) || port is < 1 or > 65535)
        {
            SetStatus("Invalid port.");
            return;
        }
        EmitSignal(SignalName.HostRequested, port, playerName, _adminPasswordEdit.Text);
    }
}
