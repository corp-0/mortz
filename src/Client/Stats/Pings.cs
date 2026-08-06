using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Net;
using Mortz.Core.Net.Stats;

namespace Mortz.Client.Stats;

/// <summary>A player's latest ping, null until an update mentions them.</summary>
public sealed class PingSample
{
    public int? Ms { get; set; }
}

/// <summary>Applies the ping stream onto players. Each update is a full table,
/// so absent players reset to no sample.</summary>
[Meta(typeof(IAutoNode))]
public partial class Pings : Node, IHandle<PingUpdateMsg>
{
    private SessionStateKey<PingSample> _ping;

    public event Action? Changed;

    public int? Of(ClientPlayer player) => player.State(_ping).Ms;

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _ping = Players.SessionKeys.Claim<PingSample>();
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public void Handle(in PingUpdateMsg message)
    {
        foreach (ClientPlayer player in Players)
        {
            player.State(_ping).Ms = null;
        }
        foreach (PeerPing ping in message.Pings)
        {
            Players.GetOrCreate(ping.PeerId).State(_ping).Ms = ping.PingMs;
        }
        Changed?.Invoke();
    }
}
