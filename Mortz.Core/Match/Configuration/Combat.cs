using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed partial class Combat
{
    [UiCategory("Mortar")]
    [UiProperty("Mortar Speed", min: 100, max: 4000, step: 50)]
    [MatchRule(min: 100, max: 4000)]
    public float MortarSpeed { get; set; } = SimConfig.MORTAR_SPEED;

    [UiProperty("Inherited Velocity", min: 0, max: 2, step: 0.05f)]
    [MatchRule(min: 0, max: 2)]
    public float MortarInherit { get; set; } = SimConfig.MORTAR_INHERIT;

    // Zero gravity makes straight shots; negative gravity bends them upward.
    [UiProperty("Mortar Gravity", min: -8000, max: 8000, step: 50)]
    [MatchRule(min: -8000, max: 8000)]
    [ZoneStat]
    public float MortarGravity { get; set; } = SimConfig.MORTAR_GRAVITY;

    [UiProperty("Mortar Max Fall Speed", min: 100, max: 4000, step: 50)]
    [MatchRule(min: 100, max: 4000)]
    public float MortarMaxFall { get; set; } = SimConfig.MORTAR_MAX_FALL;

    // Carving costs O(r^2).
    [UiProperty("Carve Radius", min: 8, max: 128)]
    [MatchRule(min: 8, max: 128)]
    public int MortarCarveRadius { get; set; } = SimConfig.MORTAR_CARVE_RADIUS;

    [UiProperty("Max Ammo", min: 1, max: 30)]
    [PlayerStat(min: 1, max: 30,
        convert: StatConvert.COUNT_BYTE, statsName: "MaxAmmo")]
    public int MortarMaxAmmo { get; set; } = SimConfig.MORTAR_MAX_AMMO;

    [UiProperty("Reload Per Shell", min: 0.1f, max: 4, step: 0.05f)]
    [PlayerStat(min: 0.1f, max: 4,
        convert: StatConvert.TICKS_BYTE, statsName: "ReloadPerShell")]
    public float MortarReloadPerShell { get; set; } = SimConfig.MORTAR_RELOAD_PER_SHELL;

    [UiCategory("Parry")]
    [UiProperty("Parry Radius", min: 8, max: 200, step: 1)]
    [PlayerStat(min: 8, max: 200)]
    public float ParryRadius { get; set; } = SimConfig.PARRY_RADIUS;

    [UiProperty("Parry Window", min: 0, max: 4, step: 0.05f)]
    [PlayerStat(min: 0, max: 4,
        convert: StatConvert.TICKS_BYTE)]
    public float ParryWindow { get; set; } = SimConfig.PARRY_WINDOW;

    [UiProperty("Parry Cooldown", min: 0, max: 120, step: 0.5f)]
    [PlayerStat(min: 0, max: 120,
        convert: StatConvert.TICKS_USHORT)]
    public float ParryCooldown { get; set; } = SimConfig.PARRY_COOLDOWN;

    [UiCategory("Health / Blast")]
    [UiProperty("Max Health", min: 1, max: 250)]
    [PlayerStat(min: 1, max: 250,
        convert: StatConvert.COUNT_BYTE)]
    public int MaxHealth { get; set; } = SimConfig.MAX_HEALTH;

    [UiProperty("Mortar Damage", min: 0, max: 250)]
    [MatchRule(min: 0, max: 250)]
    public int MortarDamage { get; set; } = SimConfig.MORTAR_DAMAGE;

    [UiProperty("Blast Core Fraction", min: 0, max: 1, step: 0.05f)]
    [MatchRule(min: 0, max: 1)]
    public float BlastCoreFraction { get; set; } = SimConfig.BLAST_CORE_FRACTION;

    [UiProperty("Blast Edge Damage", min: 0, max: 250)]
    [MatchRule(min: 0, max: 250)]
    public int BlastEdgeDamage { get; set; } = SimConfig.BLAST_EDGE_DAMAGE;

    public byte[] ToBytes() => Serialize(this);

    public static Combat FromBytes(byte[] data) => Deserialize(data);
}
