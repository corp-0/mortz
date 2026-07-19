using Mortz.Core.Match;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Core.Sim;

public class StatsPipelineTests
{
    [Fact]
    public void AddsApplyBeforeMuls_RegardlessOfListOrder()
    {
        MatchConfig cfg = new MatchConfig { MaxRunSpeed = 100 };
        PlayerStats stats = StatsPipeline.Resolve(cfg,
        [
            new StatsModifier(ModifierId.ICE, Mul(Stat.MAX_RUN_SPEED, 2f)),
            new StatsModifier(ModifierId.WATER, Add(Stat.MAX_RUN_SPEED, 50f)),
        ]);
        Assert.Equal(300f, stats.MaxRunSpeed);
    }

    [Fact]
    public void ConfigClampsCapTheComposition()
    {
        PlayerStats stats = StatsPipeline.Resolve(new MatchConfig(),
            [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 100f))]);
        Assert.Equal(200f, stats.ParryRadius); // MatchConfig clamp ceiling
    }

    [Fact]
    public void IntStatsRoundInsteadOfTruncating()
    {
        MatchConfig cfg = new MatchConfig();
        PlayerStats stats = StatsPipeline.Resolve(cfg,
            [new StatsModifier(ModifierId.WATER, Add(Stat.TOTAL_JUMPS, -1f))]);
        Assert.Equal((byte)(cfg.TotalJumps - 1), stats.TotalJumps);
    }

    [Fact]
    public void EmptyList_MatchesPlainResolve()
    {
        MatchConfig cfg = new MatchConfig { MaxRunSpeed = 123 };
        Assert.Equal(PlayerStats.Resolve(cfg).MaxRunSpeed,
            StatsPipeline.Resolve(cfg, []).MaxRunSpeed);
    }
}
