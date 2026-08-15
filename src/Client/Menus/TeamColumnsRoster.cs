using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Players;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Client.Ui;
using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
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
    public ClientPlayers Players => this.DependOn<ClientPlayers>();

    [Dependency]
    public MatchSetup Setup => this.DependOn<MatchSetup>();

    [Dependency]
    public Pings Pings => this.DependOn<Pings>();

    [Dependency]
    public SessionWins Wins => this.DependOn<SessionWins>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private IClientSender Sender => this.DependOn<IClientSender>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _team1Header.AddThemeColorOverride("font_color", TeamColors.Blue);
        _team2Header.AddThemeColorOverride("font_color", TeamColors.Red);
    }

    public void OnResolved()
    {
        Players.Changed += Render;
        Setup.SwapOffersChanged += Render;
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
        Setup.SwapOffersChanged -= Render;
        Pings.Changed -= Render;
        Wins.Changed -= Render;
        _subscribed = false;
    }

    // Frees immediately so same-frame re-renders never stack dying slots.
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
        int localId = Network.LocalPeerId;
        Team? localTeam = Players.Find(localId)?.Team;
        foreach (ClientPlayer player in Players)
        {
            VBoxContainer column = player.Team switch
            {
                Team.BLUE => _team1Slots,
                Team.RED => _team2Slots,
                _ => _unassigned,
            };
            bool acrossTheDivide = localTeam != null && player.Team != null &&
                                   player.Team != localTeam;
            column.AddChild(RosterSlots.BuildSlot(
                player, Wins.Of(player), Pings.Of(player), localId,
                acrossTheDivide ? SwapButton(player.PeerId, localId) : null,
                compact: true));
        }

        int capacity = TeamRules.SlotsPerTeam(Players.Count);
        AddEmptySlots(_team1Slots, Team.BLUE, capacity, localTeam);
        AddEmptySlots(_team2Slots, Team.RED, capacity, localTeam);
    }

    /// <summary>Offer, cancel, or accept a trade with this opponent; one
    /// message covers all three (mutual offers execute the swap).</summary>
    private Button SwapButton(int peerId, int localId)
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
        button.Pressed += () => new TeamSwapRequestMsg(peerId).SendToServer(Sender);
        return button;
    }

    private void AddEmptySlots(VBoxContainer column, Team team, int capacity,
        Team? localTeam)
    {
        for (int filled = column.GetChildCount(); filled < capacity; filled++)
        {
            column.AddChild(RosterSlots.BuildEmptySlot(localTeam != team,
                () => new TeamJoinRequestMsg(TeamWire.ToByte(team)).SendToServer(Sender)));
        }
    }
}
