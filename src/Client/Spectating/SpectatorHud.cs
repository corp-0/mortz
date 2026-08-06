using Godot;

namespace Mortz.Client.Spectating;

public partial class SpectatorHud : Control
{
    [Export] private Label _status = null!;

    public void HideStatus() => Visible = false;

    public void ShowDeathPresentation(float seconds)
    {
        Visible = true;
        _status.Text = seconds > 0
            ? $"Respawning in {seconds:0.0}"
            : "Respawning...";
    }

    public void ShowSpectating(string target, float seconds, bool canCycle)
    {
        Visible = true;
        string who = target.Length > 0 ? $"Spectating: {target}" : "Spectating";
        string respawn = seconds > 0 ? $" | Respawn in {seconds:0.0}" : "";
        string controls = canCycle ? " | A/D switch" : "";
        _status.Text = who + respawn + controls;
    }
}
