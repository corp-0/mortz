using Mortz.Client.Replication;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Protocol.Replication;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Replication;

public class MortarReplicaOrderingTests
{
    [Theory]
    [InlineData(108, 110)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void CorrectionBeforeDeflection_CannotUndoItsMotion(int correctionTick, int deflectionTick)
    {
        MortarReplicaSet replicas = Create();
        MortarState shell = Shell(1);
        replicas.Spawn(shell, unchecked(correctionTick - 2), unchecked(correctionTick - 2));
        replicas.Deflect(shell with { OwnerId = 2, Deflected = true, Velocity = new Vec2(-60, 0) },
            deflectionTick, deflectionTick);

        Assert.True(replicas.Correct(MortarWire.SerializeCorrections([shell]),
            correctionTick, deflectionTick));

        RenderMortar rendered = Assert.Single(replicas.Render());
        Assert.Equal(2, rendered.OwnerId);
        Assert.True(rendered.Deflected);
        Assert.Equal(-60, rendered.Velocity.X);
        Assert.Equal(shell.Position, rendered.Position);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(112)]
    public void DelayedDeflection_UpdatesOwnershipWithoutRewindingCorrectedMotion(int correctionTick)
    {
        MortarReplicaSet replicas = Create();
        MortarState shell = Shell(1);
        replicas.Spawn(shell, 100, 100);
        MortarState correction = shell with { Position = new Vec2(450, 500), Velocity = new Vec2(-90, 0) };
        Assert.True(replicas.Correct(MortarWire.SerializeCorrections([correction]),
            correctionTick, correctionTick));
        replicas.Tick();
        RenderMortar before = Assert.Single(replicas.Render());

        replicas.Deflect(shell with { OwnerId = 2, Deflected = true, Velocity = new Vec2(-60, 0) },
            110, correctionTick);

        RenderMortar after = Assert.Single(replicas.Render());
        Assert.Equal(2, after.OwnerId);
        Assert.True(after.Deflected);
        Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.Velocity, after.Velocity);
    }

    [Fact]
    public void Deflection_DoesNotRejectCorrectionsForOtherShells()
    {
        MortarReplicaSet replicas = Create();
        MortarState first = Shell(1);
        MortarState second = Shell(2);
        replicas.Spawn(first, 100, 100);
        replicas.Spawn(second, 100, 100);
        replicas.Deflect(first with { Velocity = new Vec2(-60, 0) }, 110, 110);

        Assert.True(replicas.Correct(MortarWire.SerializeCorrections(
            [first, second with { Velocity = new Vec2(90, 0) }]), 108, 110));

        Assert.Equal(-60, replicas.Render().Single(shell => shell.Id == 1).Velocity.X);
        Assert.Equal(90, replicas.Render().Single(shell => shell.Id == 2).Velocity.X);
    }

    [Fact]
    public void CorrectionAtDeflectionTick_CanSupplyTheFinalMotion()
    {
        MortarReplicaSet replicas = Create();
        MortarState shell = Shell(1);
        replicas.Spawn(shell, 100, 100);
        replicas.Deflect(shell with { OwnerId = 2, Deflected = true, Velocity = new Vec2(-60, 0) }, 110, 110);

        Assert.True(replicas.Correct(MortarWire.SerializeCorrections(
            [shell with { Position = new Vec2(450, 500), Velocity = new Vec2(-90, 0) }]), 110, 110));

        RenderMortar rendered = Assert.Single(replicas.Render());
        Assert.Equal(2, rendered.OwnerId);
        Assert.True(rendered.Deflected);
        Assert.Equal(new Vec2(450, 500), rendered.Position);
        Assert.Equal(-90, rendered.Velocity.X);
    }

    [Fact]
    public void CorrectionBeforeSpawn_CannotMoveAReusedShellId()
    {
        MortarReplicaSet replicas = Create();
        MortarState shell = Shell(1);
        replicas.Spawn(shell, 100, 100);
        Assert.True(replicas.TryEnd(1, out _));
        replicas.Spawn(shell with { Velocity = new Vec2(-60, 0) }, 120, 120);

        Assert.True(replicas.Correct(MortarWire.SerializeCorrections([shell]), 108, 120));

        Assert.Equal(-60, Assert.Single(replicas.Render()).Velocity.X);
    }

    private static MortarReplicaSet Create() => new(
        new TerrainMask(1_000, 1_000, (_, _) => false, (_, _) => false),
        new Combat { MortarGravity = 0 });

    private static MortarState Shell(ushort id) => new()
    {
        Id = id,
        OwnerId = 1,
        Position = new Vec2(500, 500),
        Velocity = new Vec2(60, 0),
    };
}
