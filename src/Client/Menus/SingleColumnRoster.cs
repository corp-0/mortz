using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client.Menus;

/// <summary>Free-for-all lobby roster: one slot per player, one column.</summary>
[Meta(typeof(IAutoNode))]
public partial class SingleColumnRoster : ScrollContainer
{
    [Export] private VBoxContainer _players = null!;

    private bool _subscribed;

    [Dependency]
    public IMatchSetup Setup => this.DependOn<IMatchSetup>();

    [Dependency]
    public IClientStats Stats => this.DependOn<IClientStats>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Setup.RosterChanged += Render;
        Stats.Changed += Render;
        _subscribed = true;
        Render();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Setup.RosterChanged -= Render;
        Stats.Changed -= Render;
        _subscribed = false;
    }

    // Skips the swap's own in-flight event; frees immediately so same-frame
    // re-renders never stack dying slots.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        foreach (Node child in _players.GetChildren())
            child.Free();
        long localId = Multiplayer.GetUniqueId();
        foreach (LobbyMember member in Setup.Members)
            _players.AddChild(RosterSlots.BuildSlot(member, Stats, localId));
    }
}
