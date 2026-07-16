using Godot;
using Mortz.Core;
using Mortz.Net;

namespace Mortz.Client;

/// <summary>Scene-scoped API exposed by the persistent chat node.</summary>
public interface IClientChat
{
    ChatState State { get; }
    bool IsAdmin { get; }
    IEnumerable<ChatCommandMetadata> CommandCatalog { get; }
    event Action<bool>? AdminChanged;
    bool Submit(string? input);
    bool TrySignAdminAction(byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag);
}

/// <summary>Persistent owner of client chat state, command execution, and its
/// connection lifecycle. Provides <see cref="IClientChat"/> to descendant scenes.</summary>
public partial class ClientChat : Node, IClientChat
{
    private readonly ClientChatSession _session = new();

    public ChatState State => _session.State;
    public bool IsAdmin => _session.IsAdmin;
    public IEnumerable<ChatCommandMetadata> CommandCatalog => _session.CommandCatalog;

    public event Action<bool>? AdminChanged
    {
        add => _session.AdminChanged += value;
        remove => _session.AdminChanged -= value;
    }

    public override void _Ready()
    {
        _session.Subscribe();
        NetworkManager network = NetworkManager.Instance;
        network.Connected += OnConnected;
        network.ConnectionFailed += OnSessionEnded;
        network.Disconnected += OnSessionEnded;
        network.TransportReset += OnSessionEnded;
    }

    public override void _ExitTree()
    {
        NetworkManager network = NetworkManager.Instance;
        network.Connected -= OnConnected;
        network.ConnectionFailed -= OnSessionEnded;
        network.Disconnected -= OnSessionEnded;
        network.TransportReset -= OnSessionEnded;
        _session.Dispose();
    }

    public bool Submit(string? input) => _session.Submit(input);

    public bool TrySignAdminAction(byte action, ReadOnlySpan<byte> payload,
        out ulong sequence, out byte[] tag) =>
        _session.TrySignAdminAction(action, payload, out sequence, out tag);

    private void OnConnected()
    {
        _session.Begin();
        _session.SetLocalPeerId(Multiplayer.GetUniqueId());
    }

    private void OnSessionEnded() => _session.End();
}
