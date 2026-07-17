using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Client.Ui;

namespace Mortz.Client.Menus;

/// <summary>Team lobby roster: one colored column per team. Players the
/// server has not assigned yet (a transient state around the teams toggle)
/// render full-width below the columns.</summary>
[Meta(typeof(IAutoNode))]
public partial class TeamColumnsRoster : ScrollContainer
{
    [Export] private Label _team1Header = null!;
    [Export] private Label _team2Header = null!;
    [Export] private VBoxContainer _team1Slots = null!;
    [Export] private VBoxContainer _team2Slots = null!;
    [Export] private VBoxContainer _unassigned = null!;

    private bool _subscribed;

    [Dependency]
    public IMatchSetup Setup => this.DependOn<IMatchSetup>();

    [Dependency]
    public IClientStats Stats => this.DependOn<IClientStats>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _team1Header.AddThemeColorOverride("font_color", TeamColors.Team1);
        _team2Header.AddThemeColorOverride("font_color", TeamColors.Team2);
    }

    public void OnResolved()
    {
        Setup.RosterChanged += Render;
        Setup.TeamsChanged += Render;
        Stats.Changed += Render;
        _subscribed = true;
        Render();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Setup.RosterChanged -= Render;
        Setup.TeamsChanged -= Render;
        Stats.Changed -= Render;
        _subscribed = false;
    }

    // Skips the swap's own in-flight event; frees immediately so same-frame
    // re-renders never stack dying slots.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        VBoxContainer[] columns = [_team1Slots, _team2Slots, _unassigned];
        foreach (VBoxContainer column in columns)
        {
            foreach (Node child in column.GetChildren())
                child.Free();
        }
        long localId = Multiplayer.GetUniqueId();
        foreach (LobbyMember member in Setup.Members)
        {
            VBoxContainer column = member.Team switch
            {
                1 => _team1Slots,
                2 => _team2Slots,
                _ => _unassigned,
            };
            column.AddChild(RosterSlots.BuildSlot(member, Stats, localId));
        }
    }
}
