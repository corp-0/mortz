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
    [Export] public SoundEffect? Shutdown { get; private set; }
    [Export] public SoundEffect? HolyShit { get; private set; }
    [Export] public SoundEffect? DoubleKill { get; private set; }
    [Export] public SoundEffect? TripleKill { get; private set; }
    [Export] public SoundEffect? Overkill { get; private set; }
    [Export] public SoundEffect? UltraKill { get; private set; }
    [Export] public SoundEffect? Massacre { get; private set; }
    [Export] public SoundEffect? Carnage { get; private set; }
    [Export] public SoundEffect? Bloodlust { get; private set; }
    [Export] public SoundEffect? Punishment { get; private set; }
    [Export] public SoundEffect? Dominating { get; private set; }
    [Export] public SoundEffect? MachineGod { get; private set; }
    [Export] public SoundEffect? Psycho { get; private set; }
    [Export] public SoundEffect? TeamWipe { get; private set; }
    [Export] public SoundEffect? SuicideMock { get; private set; }
}
