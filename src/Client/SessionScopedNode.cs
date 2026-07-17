using Godot;
using Mortz.Net;

namespace Mortz.Client;

/// <summary>
/// Base for the client state-service nodes, whose state lives for exactly one
/// server connection. Carries the boundary ceremony once: every connection
/// edge (connected, failed, disconnected, transport reset) triggers
/// OnSessionBoundary; OnServiceReady/OnServiceExit wrap the node's own
/// subscriptions.
/// </summary>
public abstract partial class SessionScopedNode : Node
{
    public sealed override void _Ready()
    {
        OnServiceReady();
        NetworkManager network = NetworkManager.Instance;
        network.Connected += OnSessionBoundary;
        network.ConnectionFailed += OnSessionBoundary;
        network.Disconnected += OnSessionBoundary;
        network.TransportReset += OnSessionBoundary;
    }

    public sealed override void _ExitTree()
    {
        NetworkManager network = NetworkManager.Instance;
        network.Connected -= OnSessionBoundary;
        network.ConnectionFailed -= OnSessionBoundary;
        network.Disconnected -= OnSessionBoundary;
        network.TransportReset -= OnSessionBoundary;
        OnServiceExit();
    }

    protected abstract void OnServiceReady();
    protected abstract void OnServiceExit();
    protected abstract void OnSessionBoundary();
}
