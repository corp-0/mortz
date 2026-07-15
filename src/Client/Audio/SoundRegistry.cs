using Godot;

namespace Mortz.Client;

[GlobalClass]
public partial class SoundRegistry : Resource
{
    [ExportGroup("Weapons")]
    [Export] public SoundEffect MortarFire { get; private set; } = null!;
    [Export] public SoundEffect MortarReload { get; private set; } = null!;
    [Export] public SoundEffect ShellWhoosh { get; private set; } = null!;
    [Export] public SoundEffect ShellImpact { get; private set; } = null!;
    [Export] public SoundEffect DeathScream { get; private set; } = null!;

    [ExportGroup("Parry")]
    [Export] public SoundEffect ParryRaise { get; private set; } = null!;
    [Export] public SoundEffect ParrySuccess { get; private set; } = null!;

    [ExportGroup("Announcer")]
    [Export] public SoundEffect RegularKill { get; private set; } = null!;
    [Export] public SoundEffect FirstBlood { get; private set; } = null!;
    [Export] public SoundEffect Owned { get; private set; } = null!;

    public IEnumerable<(string Name, SoundEffect Sound)> Entries()
    {
        yield return (nameof(MortarFire), MortarFire);
        yield return (nameof(MortarReload), MortarReload);
        yield return (nameof(ShellWhoosh), ShellWhoosh);
        yield return (nameof(ShellImpact), ShellImpact);
        yield return (nameof(DeathScream), DeathScream);
        yield return (nameof(ParryRaise), ParryRaise);
        yield return (nameof(ParrySuccess), ParrySuccess);
        yield return (nameof(RegularKill), RegularKill);
        yield return (nameof(FirstBlood), FirstBlood);
        yield return (nameof(Owned), Owned);
    }
}
