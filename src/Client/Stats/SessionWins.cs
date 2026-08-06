using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Core.Net;
using Mortz.Core.Net.Stats;

namespace Mortz.Client.Stats;

/// <summary>A player's wins this session as last told by the server.</summary>
public sealed class WinCount
{
    public int Value { get; set; }
}

/// <summary>Applies the session-wins stream onto players. Each broadcast is a
/// full table, so absent players reset to zero.</summary>
[Meta(typeof(IAutoNode))]
public partial class SessionWins : Node, IHandle<SessionWinsMsg>
{
    private SessionStateKey<WinCount> _wins;

    public event Action? Changed;

    public int Of(ClientPlayer player) => player.State(_wins).Value;

    [Dependency]
    private ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _wins = Players.SessionKeys.Claim<WinCount>();
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public void Handle(in SessionWinsMsg message)
    {
        if (!SessionStatsProtocol.TryDecode(message, out PeerWins[]? wins))
            return;
        foreach (ClientPlayer player in Players)
        {
            player.State(_wins).Value = 0;
        }
        foreach (PeerWins win in wins)
        {
            Players.GetOrCreate(win.PeerId).State(_wins).Value = win.Wins;
        }
        Changed?.Invoke();
    }
}
