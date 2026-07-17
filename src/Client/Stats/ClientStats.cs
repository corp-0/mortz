using Godot;
using Mortz.Net;

namespace Mortz.Client.Stats;

/// <summary>Persistent owner of per-player session stats and their connection
/// lifecycle. Provides <see cref="IClientStats"/> to descendant scenes.</summary>
public partial class ClientStats : Node, IClientStats
{
    private readonly ClientStatsSession _session = new();

    public event Action? Changed
    {
        add => _session.Changed += value;
        remove => _session.Changed -= value;
    }

    public int? PingMs(long peerId) => _session.PingMs(peerId);
    public int Wins(long peerId) => _session.Wins(peerId);

    public override void _Ready()
    {
        _session.Subscribe();
        NetworkManager network = NetworkManager.Instance;
        network.Connected += OnSessionBoundary;
        network.ConnectionFailed += OnSessionBoundary;
        network.Disconnected += OnSessionBoundary;
        network.TransportReset += OnSessionBoundary;
    }

    public override void _ExitTree()
    {
        NetworkManager network = NetworkManager.Instance;
        network.Connected -= OnSessionBoundary;
        network.ConnectionFailed -= OnSessionBoundary;
        network.Disconnected -= OnSessionBoundary;
        network.TransportReset -= OnSessionBoundary;
        _session.Dispose();
    }

    private void OnSessionBoundary() => _session.Clear();
}
