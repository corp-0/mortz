using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Match;
using Mortz.Client.Views;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Sim.Modifiers;
using Mortz.Net;
using Mortz.Tests.Core;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class PlayerViewStatsTests : NodeServiceTest
{
    [Fact]
    public void PerPlayerModifiersConfigureTheMatchingView()
    {
        PlayerViewManager manager = TakeManagerFromGameViewScene();
        manager.FakeDependency<INetwork>(new FakeNetwork());
        Host(manager);
        manager.Configure(new MatchConfig());

        float baseRadius = TestWorlds.Stats.ParryRadius;
        byte[] bigParry = ModifierWire.Serialize(
            [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 4f))]);
        byte[] smallParry = ModifierWire.Serialize(
            [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 0.5f))]);

        new PlayerModifiersMsg(2, bigParry).Broadcast();
        manager.BeginFrame();
        manager.Place(2, ViewState());
        manager.Place(3, ViewState());

        Assert.Equal(baseRadius * 4, manager.ViewForTest(2).StatsForTest.ParryRadius);
        Assert.Equal(baseRadius, manager.ViewForTest(3).StatsForTest.ParryRadius);

        new PlayerModifiersMsg(3, smallParry).Broadcast();
        Assert.Equal(baseRadius * 0.5f, manager.ViewForTest(3).StatsForTest.ParryRadius);
        Assert.Equal(baseRadius * 4, manager.ViewForTest(2).StatsForTest.ParryRadius);
    }

    private static PlayerViewManager TakeManagerFromGameViewScene()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/Match/GameView.tscn").Instantiate<GameView>();
        PlayerViewManager manager = shell.GetNode<PlayerViewManager>("Players");
        shell.RemoveChild(manager);
        shell.Free();
        return manager;
    }

    private static PlayerViewState ViewState() => new(
        Feet: new Vector2(200, 240), Aim: 0, Skin: 0, Ammo: 3, ReloadTicks: 0,
        Health: 3, RespawnTicks: 0, ParryTicks: 0, DashCooldown: 0, SpawnImmunityTicks: 0);
}
