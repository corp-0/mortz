using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.Core.Ui;

namespace Mortz.Core.Match.Configuration;

public sealed partial class ModeRules
{
    [UiCategory("Mode")]
    [UiProperty("Teams")]
    [MatchRule]
    public bool Teams { get; set; }

    public VictoryRules Victory { get; set; } = new KillsVictoryRules();

    // Self-damage always applies.
    [UiProperty("Friendly Fire")]
    [UiVisibleWhen(nameof(FriendlyFireIsRelevant))]
    [MatchRule]
    public bool FriendlyFire { get; set; } = true;

    [UiProperty("Suicide Penalty")]
    [MatchRule]
    public SuicidePenalty SuicidePenalty { get; set; } = SuicidePenalty.NONE;

    [UiProperty("Spectate During Respawn")]
    [MatchRule]
    public bool SpectateDuringRespawn { get; set; } = true;

    [UiCategory("Respawn")]
    [UiProperty("Respawn Delay", min: 0, max: 60, step: 0.05f)]
    [MatchRule(min: 0, max: 60)]
    public float RespawnDelay { get; set; } = SimConfig.RESPAWN_DELAY;

    [UiProperty("Spawn Immunity", min: 0, max: 4, step: 0.05f)]
    [MatchRule(min: 0, max: 4)]
    public float SpawnImmunity { get; set; } = SimConfig.SPAWN_IMMUNITY;

    public int RespawnDelayTicks => (int)(RespawnDelay * SimConfig.TICK_RATE);

    public int SpawnImmunityTicks => (int)(SpawnImmunity * SimConfig.TICK_RATE);

    public bool FriendlyFireIsRelevant => Teams;

    public byte[] ToBytes()
    {
        byte[] shared = Serialize(this);
        byte[] victory = VictoryRulesMetadata.Serialize(Victory);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(shared.Length);
        writer.Write(shared);
        writer.Write(victory);
        return stream.ToArray();
    }

    public static ModeRules FromBytes(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        int sharedLength = reader.ReadInt32();
        if (sharedLength < 0 || sharedLength > stream.Length - stream.Position)
            throw new InvalidDataException("Invalid shared match-rules length.");
        ModeRules rules = Deserialize(reader.ReadBytes(sharedLength));
        rules.Victory = VictoryRulesMetadata.Deserialize(
            reader.ReadBytes(checked((int)(stream.Length - stream.Position))));
        return rules;
    }
}
