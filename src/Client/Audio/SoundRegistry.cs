using Godot;

namespace Mortz.Client.Audio;

[GlobalClass]
public partial class SoundRegistry : Resource
{
    [ExportGroup("Weapons")]
    [Export] public SoundEffect? MortarFire { get; private set; }
    [Export] public SoundEffect? MortarReload { get; private set; }
    [Export] public SoundEffect? ShellWhoosh { get; private set; }
    [Export] public SoundEffect? ShellImpact { get; private set; }
    [Export] public SoundEffect? DeathScream { get; private set; }

    [ExportGroup("Parry")]
    [Export] public SoundEffect? ParryRaise { get; private set; }
    [Export] public SoundEffect? ParrySuccess { get; private set; }

    [ExportGroup("Announcer")]
    [Export] public SoundEffect? RegularKill { get; private set; }
    [Export] public SoundEffect? FirstBlood { get; private set; }
    [Export] public SoundEffect? Owned { get; private set; }
}
