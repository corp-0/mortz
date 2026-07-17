using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Stats;

namespace Mortz.Client.Menus;

/// <summary>Builds the shared lobby player-slot row (name, session wins, ping,
/// ready state) so every roster layout renders identical slots.</summary>
internal static class RosterSlots
{
    public static Control BuildSlot(LobbyMember member, IClientStats stats, long localId,
        Control? action = null)
    {
        string self = member.PeerId == localId ? " (you)" : "";
        PanelContainer slot = new() { CustomMinimumSize = new Vector2(0, 44) };
        StyleBoxFlat background = new()
        {
            BgColor = new Color("111827"),
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusBottomLeft = 5,
        };
        slot.AddThemeStyleboxOverride("panel", background);

        MarginContainer margin = new();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 14);
        margin.AddChild(row);
        slot.AddChild(margin);

        row.AddChild(new Label
        {
            Text = $"{member.Name}{self}",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        int wins = stats.Wins(member.PeerId);
        row.AddChild(StatLabel(wins == 1 ? "1 WIN" : $"{wins} WINS", new Color("fbbf24"), 64));
        row.AddChild(StatLabel(
            stats.PingMs(member.PeerId) is { } ping ? $"{ping} ms" : "... ms",
            new Color("64748b"), 64));
        row.AddChild(StatLabel(member.Ready ? "READY" : "WAITING",
            member.Ready ? new Color("86efac") : new Color("94a3b8"), 80));
        if (action != null)
            row.AddChild(action);
        return slot;
    }

    /// <summary>A free team slot; clicking it asks the server for the move.</summary>
    public static Control BuildEmptySlot(bool enabled, Action pressed)
    {
        Button slot = new()
        {
            Text = "JOIN",
            Disabled = !enabled,
            CustomMinimumSize = new Vector2(0, 44),
            Modulate = new Color(1, 1, 1, 0.55f),
        };
        slot.Pressed += pressed;
        return slot;
    }

    private static Label StatLabel(string text, Color color, int minWidth) => new()
    {
        Text = text,
        Modulate = color,
        HorizontalAlignment = HorizontalAlignment.Right,
        CustomMinimumSize = new Vector2(minWidth, 0),
    };
}
