using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Core.Sim;

public class PlayerStatCompositionTests
{
    [Fact]
    public void ResolveEffective_AppendsSituationsThenZonesInDeclarationOrder()
    {
        MatchConfig config = new();
        StatsModifier[] persistent =
        [
            new(ModifierId.ICE, Mul(Stat.GRAVITY, 1.0001f)),
            new(ModifierId.WATER, Mul(Stat.GRAVITY, 0.9999f)),
        ];
        MapZones zones = new(
        [
            new MapZone("first", [], ZoneShape.Rect(0, 0, 1, 1),
                new StatsModifier(ModifierId.ZONE, Mul(Stat.GRAVITY, 1.0002f))),
            new MapZone("second", [], ZoneShape.Rect(0, 0, 1, 1),
                new StatsModifier(ModifierId.ZONE, Mul(Stat.GRAVITY, 0.9998f))),
        ]);
        StatsModifier[] expectedOrder =
        [
            .. persistent,
            Modifiers.Ice,
            Modifiers.Water,
            Modifiers.Special,
            zones.EffectZones[0].Effects!,
            zones.EffectZones[1].Effects!,
        ];

        PlayerStats expected = StatsPipeline.Resolve(config, expectedOrder);
        PlayerStats actual = PlayerStatComposition.ResolveEffective(
            config, persistent,
            Situations.ON_ICE | Situations.IN_WATER | Situations.SPECIAL,
            0b11, zones);

        Assert.Equal(expected.Gravity, actual.Gravity);
        Assert.Equal(expected.GroundFriction, actual.GroundFriction);
        Assert.Equal(expected.MaxRunSpeed, actual.MaxRunSpeed);
    }

    [Fact]
    public void ResolveEffective_WithoutOverlaysReturnsCachedPersistentStats()
    {
        MatchConfig config = new();
        StatsModifier[] persistent =
        [
            new(ModifierId.SPECIAL, Mul(Stat.MAX_RUN_SPEED, 1.25f)),
        ];
        PlayerStats persistentStats = StatsPipeline.Resolve(config, persistent);

        PlayerStats actual = PlayerStatComposition.ResolveEffective(
            config, persistent, persistentStats, Situations.NONE, 0, MapZones.None);

        Assert.Same(persistentStats, actual);
    }
}
