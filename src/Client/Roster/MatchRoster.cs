using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net;

namespace Mortz.Client.Roster;

/// <summary>Connected-session cache of the server's match roster broadcasts.
/// Fed by the match stream only; the lobby renders from MatchSetup.</summary>
public partial class MatchRoster : Node
{
    public event Action? Changed;

    public RosterSnapshot Table { get; private set; } = RosterSnapshot.EMPTY;

    public string NameOf(long peerId) =>
        Table.TryFind(peerId, out RosterEntry entry) ? entry.Name : Unknown(peerId);

    /// <summary>Null when the player is unknown or teams are off.</summary>
    public Team? TeamOf(long peerId) =>
        Table.TryFind(peerId, out RosterEntry entry) ? entry.Team : null;

    public byte SkinOf(long peerId) =>
        Table.TryFind(peerId, out RosterEntry entry) ? entry.Skin : (byte)0;

    public override void _Ready() => RosterProtocol.MatchRosterReceived += OnRoster;

    public override void _ExitTree() => RosterProtocol.MatchRosterReceived -= OnRoster;

    private void OnRoster(RosterSnapshot snapshot)
    {
        Table = snapshot;
        Changed?.Invoke();
    }

    // Deliberately unlike a real name so a lookup miss cannot pass for one.
    private static string Unknown(long peerId) => $"<unknown {peerId}>";
}
