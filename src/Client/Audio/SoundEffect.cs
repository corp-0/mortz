using Godot;

namespace Mortz.Client.Audio;

public enum SoundPriority
{
    AMBIENT,
    GAMEPLAY,
    CRITICAL,
}

public enum SoundBus
{
    SFX,
    UI,
    MUSIC,
    DEATH_SFX,
}

public static class SoundBusExtensions
{
    public static StringName ToGodotName(this SoundBus bus) => bus switch
    {
        SoundBus.SFX => "Sfx",
        SoundBus.UI => "UI",
        SoundBus.MUSIC => "Music",
        SoundBus.DEATH_SFX => "DeathSfx",
        _ => throw new ArgumentOutOfRangeException(nameof(bus), bus, null),
    };
}

[GlobalClass]
public partial class SoundEffect : Resource
{
    [Export] public AudioStream? Stream { get; private set; }
    [Export(PropertyHint.Range, "-40,10")] public float VolumeDb { get; private set; }
    [Export] public SoundBus Bus { get; private set; } = SoundBus.SFX;
    public StringName BusName => Bus.ToGodotName();
    [Export] public float MaxDistance { get; private set; } = 2200f;
    [Export] public SoundPriority Priority { get; private set; } = SoundPriority.GAMEPLAY;
    /// <summary>Whether pitch follows the client-only presentation clock
    /// (currently the final-kill replay). Announcements opt out.</summary>
    [Export] public bool TimeScaled { get; private set; } = true;
}
