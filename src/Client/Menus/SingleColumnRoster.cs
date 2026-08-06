using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Client.Stats;
using Mortz.Net;

namespace Mortz.Client.Menus;

/// <summary>Free-for-all lobby roster: one slot per player, one column.</summary>
[Meta(typeof(IAutoNode))]
public partial class SingleColumnRoster : ScrollContainer
{
    [Export] private VBoxContainer _players = null!;

    private bool _subscribed;

    [Dependency]
    public ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    public Pings Pings => this.DependOn<Pings>();

    [Dependency]
    public SessionWins Wins => this.DependOn<SessionWins>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Players.Changed += Render;
        Pings.Changed += Render;
        Wins.Changed += Render;
        _subscribed = true;
        Render();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Players.Changed -= Render;
        Pings.Changed -= Render;
        Wins.Changed -= Render;
        _subscribed = false;
    }

    // Frees immediately so same-frame re-renders never stack dying slots.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        foreach (Node child in _players.GetChildren())
        {
            child.Free();
        }
        int localId = Network.LocalPeerId;
        foreach (ClientPlayer player in Players)
        {
            _players.AddChild(RosterSlots.BuildSlot(
                player, Wins.Of(player), Pings.Of(player), localId));
        }
    }
}
