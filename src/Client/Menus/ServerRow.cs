using Godot;
using Mortz.Client.Servers;
using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Menus;

/// <summary>One line of the server browser, instanced per entry.</summary>
public partial class ServerRow : Button
{
    [Signal] public delegate void SelectedEventHandler();
    [Signal] public delegate void FavoriteToggledEventHandler();

    [Export] private ColorRect _statusDot = null!;
    [Export] private Label _name = null!;
    [Export] private Label _detail = null!;
    [Export] private Label _population = null!;
    [Export] private Label _ping = null!;
    [Export] private Button _star = null!;

    public ServerEntry Entry { get; private set; } = null!;

    public void Bind(ServerEntry entry)
    {
        Entry = entry;
        Refresh();
    }

    public void Refresh()
    {
        _name.Text = Entry.DisplayName;
        _statusDot.Color = StatusColor();
        // An unnamed row already shows the address as its title.
        _detail.Text = Entry.HasName ? $"{Entry.Endpoint} - {StatusText()}" : StatusText();
        _population.Text = Entry.Info is ServerInfo info ? $"{info.Players}/{info.MaxPlayers}" : "-";
        _ping.Text = Entry.Status == ServerStatus.ONLINE ? $"{Entry.PingMs} ms" : "";
        _star.Text = Entry.IsFavorite ? "★" : "☆";
        _star.Disabled = !Entry.CanToggleFavorite;
        _star.TooltipText = StarTooltip();
    }

    public void SetSelected(bool selected) => ButtonPressed = selected;

    // ---- button handlers (connected in ServerRow.tscn) ----

    public void OnPressed() => EmitSignal(SignalName.Selected);

    public void OnStarPressed() => EmitSignal(SignalName.FavoriteToggled);

    private string StarTooltip()
    {
        if (!Entry.CanToggleFavorite)
            return "Always available";
        return Entry.IsFavorite ? "Remove from favorites" : "Keep in favorites";
    }

    private Color StatusColor() => GetThemeColor(Entry.Status switch
    {
        ServerStatus.ONLINE => "success",
        ServerStatus.INCOMPATIBLE => "caution",
        ServerStatus.OFFLINE => "danger",
        _ => "pending"
    }, "MortzUI");

    private string StatusText()
    {
        switch (Entry.Status)
        {
            case ServerStatus.PROBING:
                return "pinging...";
            case ServerStatus.OFFLINE:
                return "no response";
            case ServerStatus.INCOMPATIBLE:
                return "different game version";
            case ServerStatus.UNKNOWN when Entry.Info != null:
                return "compatibility unknown";
            case ServerStatus.ONLINE when Entry.Info is ServerInfo info:
                string phase;
                if (info.InLobby)
                    phase = "in lobby";
                else if (info.AllowJoinInProgress)
                    phase = "in match - JIP enabled";
                else
                    phase = "in match - spectators only";
                return $"{info.Mode} - {info.Map} - {phase}";
            default:
                return "not checked";
        }
    }
}
