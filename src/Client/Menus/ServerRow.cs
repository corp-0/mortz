using Godot;
using Mortz.Client.Servers;
using Mortz.Core.Net.Query;

namespace Mortz.Client.Menus;

/// <summary>One line of the server browser, instanced per entry.</summary>
public partial class ServerRow : Button
{
    private static readonly Color _online = new(0.35f, 0.85f, 0.45f);
    private static readonly Color _probing = new(0.6f, 0.6f, 0.6f);
    private static readonly Color _offline = new(0.85f, 0.3f, 0.3f);
    private static readonly Color _incompatible = new(0.95f, 0.7f, 0.2f);

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

    private Color StatusColor() => Entry.Status switch
    {
        ServerStatus.ONLINE => _online,
        ServerStatus.PROBING => _probing,
        ServerStatus.INCOMPATIBLE => _incompatible,
        ServerStatus.OFFLINE => _offline,
        _ => _probing,
    };

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
            case ServerStatus.ONLINE when Entry.Info is ServerInfo info:
                return $"{info.Mode} - {info.Map} - {(info.InLobby ? "in lobby" : "in match")}";
            default:
                return "not checked";
        }
    }
}
