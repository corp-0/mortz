using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Client.Ui;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Menus;

/// <summary>Team lobby roster: one colored column per team, with free slots
/// rendered as JOIN buttons that request the move (the server validates
/// capacity). Players the server has not assigned yet (a transient state
/// around the teams toggle) render full-width below the columns.</summary>
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
    public MatchSetup Setup => this.DependOn<MatchSetup>();

    [Dependency]
    public ClientStats Stats => this.DependOn<ClientStats>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

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
        Setup.SwapOffersChanged += Render;
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
        Setup.SwapOffersChanged -= Render;
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
            {
                child.Free();
            }
        }
        long localId = Network.LocalPeerId;
        byte localTeam = 0;
        foreach (LobbyMember member in Setup.Members)
        {
            if (member.PeerId == localId)
                localTeam = member.Team;
        }
        foreach (LobbyMember member in Setup.Members)
        {
            VBoxContainer column = member.Team switch
            {
                1 => _team1Slots,
                2 => _team2Slots,
                _ => _unassigned,
            };
            bool acrossTheDivide = localTeam != 0 && member.Team != 0 &&
                                   member.Team != localTeam;
            column.AddChild(RosterSlots.BuildSlot(member, Stats, localId,
                acrossTheDivide ? SwapButton(member.PeerId, localId) : null,
                compact: true));
        }

        int capacity = TeamRules.SlotsPerTeam(Setup.Members.Count);
        AddEmptySlots(_team1Slots, 1, capacity, localTeam);
        AddEmptySlots(_team2Slots, 2, capacity, localTeam);
    }

    /// <summary>Offer, cancel, or accept a trade with this opponent; one
    /// message covers all three (mutual offers execute the swap).</summary>
    private Button SwapButton(long peerId, long localId)
    {
        bool outgoing = Setup.SwapOffers.Contains(new SwapOffer(localId, peerId));
        bool incoming = Setup.SwapOffers.Contains(new SwapOffer(peerId, localId));
        string text = "SWAP";
        if (outgoing)
            text = "CANCEL";
        else if (incoming)
            text = "ACCEPT";
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(84, 0),
        };
        if (incoming)
            button.Modulate = new Color("86efac"); // the READY accent
        button.Pressed += () => new TeamSwapRequestMsg(peerId).SendToServer();
        return button;
    }

    private static void AddEmptySlots(VBoxContainer column, byte team, int capacity,
        byte localTeam)
    {
        for (int filled = column.GetChildCount(); filled < capacity; filled++)
        {
            column.AddChild(RosterSlots.BuildEmptySlot(localTeam != team,
                () => new TeamJoinRequestMsg(team).SendToServer()));
        }
    }
}
