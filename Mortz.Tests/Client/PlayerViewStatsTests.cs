using Godot;
using Mortz.Client.Views;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Sim.Modifiers;
using Mortz.Tests.Core;
using twodog.xunit;
using Xunit;
using static Mortz.Core.Sim.Modifiers.StatChange;

namespace Mortz.Tests.Client;

/// <summary>Replicated per-player modifier lists reach the matching
/// PlayerView whether the modifiers or the view arrive first, and never
/// bleed onto other players.</summary>
[Collection(nameof(GodotHeadlessCollection))]
public class PlayerViewStatsTests
{
    [Fact]
    public void PerPlayerModifiers_ConfigureTheMatchingView()
    {
        SceneTree tree = Assert.IsType<SceneTree>(Engine.GetMainLoop());
        PlayerViewManager manager = new PlayerViewManager();
        manager.SetPlayerSceneForTest(
            ResourceLoader.Load<PackedScene>("res://src/Shared/Prefabs/Player.tscn"));
        manager.Configure(new MatchConfig());
        tree.Root.AddChild(manager);
        try
        {
            float baseRadius = TestWorlds.Stats.ParryRadius;
            byte[] bigParry = ModifierWire.Serialize(
                [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 4f))]);
            byte[] smallParry = ModifierWire.Serialize(
                [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 0.5f))]);

            // Peer 2's modifiers land before its view exists, peer 3's only after.
            manager.ApplyModifiersForTest(new PlayerModifiersMsg(2, bigParry));
            manager.BeginFrame();
            manager.Place(2, ViewState());
            manager.Place(3, ViewState());

            Assert.Equal(baseRadius * 4, manager.ViewForTest(2).StatsForTest.ParryRadius);
            Assert.Equal(baseRadius, manager.ViewForTest(3).StatsForTest.ParryRadius);

            manager.ApplyModifiersForTest(new PlayerModifiersMsg(3, smallParry));
            Assert.Equal(baseRadius * 0.5f, manager.ViewForTest(3).StatsForTest.ParryRadius);
            Assert.Equal(baseRadius * 4, manager.ViewForTest(2).StatsForTest.ParryRadius);
        }
        finally
        {
            manager.Free();
        }
    }

    private static PlayerViewState ViewState() => new(
        Feet: new Vector2(200, 240), Aim: 0, Skin: 0, Ammo: 3, ReloadTicks: 0,
        Health: 3, RespawnTicks: 0, ParryTicks: 0, DashCooldown: 0, SpawnImmunityTicks: 0);
}
