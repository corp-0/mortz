using Mortz.Core.Sim;

namespace Mortz.Client.Views;

/// <summary>A player's view-feature state: resolved stats, typing flag, and
/// the live view while one is on screen. Stats stay null until the player's
/// modifier list arrives.</summary>
public sealed class ViewSlot
{
    public PlayerStats? Stats { get; set; }
    public bool Typing { get; set; }
    public PlayerView? View { get; set; }
}
