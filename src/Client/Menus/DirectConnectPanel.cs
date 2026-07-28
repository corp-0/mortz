using Godot;
using Mortz.Core.Net;
using Mortz.Core.Net.Query;

namespace Mortz.Client.Menus;

/// <summary>
/// Type an address, probe it, then join, or join blind when only the game
/// port is forwarded. The browser runs the probe and reports back through
/// Found and NotFound.
/// </summary>
public partial class DirectConnectPanel : PanelContainer
{
    [Signal] public delegate void FindRequestedEventHandler(string address, int port);
    [Signal] public delegate void JoinRequestedEventHandler(string address, int port);

    [Export] private LineEdit _address = null!;
    [Export] private LineEdit _port = null!;
    [Export] private Label _status = null!;
    [Export] private Button _joinAnyway = null!;

    public override void _Ready() => _port.Text = NetConfig.DEFAULT_PORT.ToString();

    public void Open()
    {
        Show();
        _status.Text = "";
        _joinAnyway.Hide();
        _address.GrabFocus();
    }

    public void Found() => Hide();

    public void NotFound()
    {
        _status.Text = "No response. It may be offline, or only the game port is open.";
        _joinAnyway.Show();
    }

    // ---- button handlers (connected in DirectConnectPanel.tscn) ----

    public void OnFindPressed()
    {
        if (!TryReadEndpoint(out ServerEndpoint endpoint))
            return;
        _status.Text = $"Looking for {endpoint}...";
        _joinAnyway.Hide();
        EmitSignal(SignalName.FindRequested, endpoint.Address, endpoint.Port);
    }

    /// <summary>A host who forwarded only the game port answers no query but
    /// is still joinable.</summary>
    public void OnJoinAnywayPressed()
    {
        if (TryReadEndpoint(out ServerEndpoint endpoint))
            EmitSignal(SignalName.JoinRequested, endpoint.Address, endpoint.Port);
    }

    public void OnCancelPressed() => Hide();

    private bool TryReadEndpoint(out ServerEndpoint endpoint)
    {
        endpoint = default;
        string address = _address.Text.Trim();
        string portText = _port.Text.Trim();
        int colon = address.LastIndexOf(':');
        if (colon >= 0)
        {
            portText = address[(colon + 1)..];
            address = address[..colon];
        }
        if (address.Length == 0)
        {
            _status.Text = "Enter a server address.";
            return false;
        }
        if (!int.TryParse(portText, out int port) || port is < 1 or > 65535)
        {
            _status.Text = "Invalid port.";
            return false;
        }
        endpoint = new ServerEndpoint(address, port);
        return true;
    }
}
