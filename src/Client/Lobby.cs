using Godot;

namespace Mortz.Client;

/// <summary>
/// Title menu: Host or Join by IP. Pure UI; ClientMain listens to the signals
/// and runs the actual connection.
/// </summary>
public partial class Lobby : Control
{
    [Signal] public delegate void HostRequestedEventHandler();
    [Signal] public delegate void JoinRequestedEventHandler(string address);

    [Export] private LineEdit _addressEdit = null!;
    [Export] private Label _status = null!;

    public void SetStatus(string text) => _status.Text = text;

    public void OnHostPressed() => EmitSignal(SignalName.HostRequested);

    public void OnJoinPressed() => EmitSignal(SignalName.JoinRequested, _addressEdit.Text.Trim());
}
