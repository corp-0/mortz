using Godot;
using Mortz.Client.Servers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Menus;

[GlobalClass]
public partial class DirectConnectPanel : PanelContainer
{
    [Signal] public delegate void FindRequestedEventHandler(string address, string port, string queryPort);
    [Signal] public delegate void JoinRequestedEventHandler(string address, string port);
    [Signal] public delegate void CancelRequestedEventHandler();

    [Export] private LineEdit _address = null!;
    [Export] private LineEdit _port = null!;
    [Export] private LineEdit _queryPort = null!;
    [Export] private Label _status = null!;
    [Export] private Button _joinAnyway = null!;

    public override void _Ready() => _port.Text = NetConfig.DEFAULT_PORT.ToString();
    public void FocusAddress() => _address.GrabFocus();

    public void Render(DirectConnectState state, ServerAddressError error, ServerEndpoint? endpoint)
    {
        Visible = state != DirectConnectState.CLOSED;
        _joinAnyway.Visible = state == DirectConnectState.NOT_FOUND;
        _status.Text = error switch
        {
            ServerAddressError.ADDRESS_REQUIRED => "Enter a valid server address.",
            ServerAddressError.INVALID_PORT => "Invalid port.",
            ServerAddressError.QUERY_PORT_REQUIRED => $"Enter a query port when the game port is {ushort.MaxValue}.",
            ServerAddressError.INVALID_QUERY_PORT => "Invalid query port.",
            ServerAddressError.SAME_PORT => "Game and query ports must differ.",
            _ => state switch
            {
                DirectConnectState.PROBING => $"Looking for {endpoint}...",
                DirectConnectState.NOT_FOUND => "No response. It may be offline, or only the game port is open.",
                _ => "",
            },
        };
    }

    public void OnFindPressed() =>
        EmitSignal(SignalName.FindRequested, _address.Text, _port.Text, _queryPort.Text);
    public void OnJoinAnywayPressed() => EmitSignal(SignalName.JoinRequested, _address.Text, _port.Text);
    public void OnCancelPressed() => EmitSignal(SignalName.CancelRequested);
}
