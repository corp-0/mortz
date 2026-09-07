using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class MatchTickTests
{
    [Fact]
    public void CompletionFreezesOutputs()
    {
        using MatchRuntime runtime = NewRuntime();
        runtime.World.QueueDamage(1, byte.MaxValue);
        MatchUpdate update = runtime.Advance(new ServerTime(100, 0.5));
        Death expected = Assert.Single(update.Deaths);
        runtime.Advance(default);
        Assert.Empty(runtime.World.Deaths);
        Assert.Equal(expected, Assert.Single(update.Deaths));
        Assert.Equal(new ServerTime(100, 0.5), update.Time);
    }

    private static MatchRuntime NewRuntime()
    {
        MatchStateKeys keys = new(1);
        MatchRuntime runtime = new(new TerrainMask(64, 64, (_, _) => false, (_, _) => false),
            new MatchConfig { Rules = new ModeRules { SpawnImmunity = 0 } }, 10, keys);
        Player player = new(1, "player", 0, 1);
        player.OpenMatch(keys.Count, keys.Generation);
        runtime.Seat(player);
        return runtime;
    }
}
