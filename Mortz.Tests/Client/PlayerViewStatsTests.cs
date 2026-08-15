using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net.Sim;
using Mortz.Core.Replication;
using Mortz.Core.Sim.Modifiers;
using Mortz.Net;
using Mortz.Tests.Core;
using Mortz.Tests.Net;
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
        manager.FakeDependency<ISfx>(new NullSfx());
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        manager.FakeDependency(players);
        HostRouted(manager);

        float baseRadius = TestWorlds.Stats.ParryRadius;
        byte[] bigParry = ModifierWire.Serialize(
            [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 4f))]);
        byte[] smallParry = ModifierWire.Serialize(
            [new StatsModifier(ModifierId.SPECIAL, Mul(Stat.PARRY_RADIUS, 0.5f))]);

        new PlayerModifiersMsg(2, bigParry).Broadcast(Router);
        manager.BeginFrame();
        manager.Place(2, ViewState());
        manager.Place(3, ViewState());

        Assert.Equal(baseRadius * 4, manager.ViewForTest(2).StatsForTest.ParryRadius);
        Assert.Equal(baseRadius, manager.ViewForTest(3).StatsForTest.ParryRadius);

        new PlayerModifiersMsg(3, smallParry).Broadcast(Router);
        Assert.Equal(baseRadius * 0.5f, manager.ViewForTest(3).StatsForTest.ParryRadius);
        Assert.Equal(baseRadius * 4, manager.ViewForTest(2).StatsForTest.ParryRadius);
    }

    [Fact]
    public void ApplyComposesTheSampledPresentationEffects()
    {
        PlayerViewManager manager = TakeManagerFromGameViewScene();
        manager.FakeDependency<INetwork>(new FakeNetwork());
        manager.FakeDependency<ISfx>(new NullSfx());
        ClientPlayers players = HostRouted(new ClientPlayers());
        players.OpenMatch(new MatchConfig());
        manager.FakeDependency(players);
        HostRouted(manager);

        manager.BeginFrame();
        manager.Place(2, ViewState() with
        {
            Presentation = new PlayerPresentationState
            {
                KillingSpreeMagnitude = 5,
                IsBleeding = true,
            },
        });

        PlayerView view = manager.ViewForTest(2);
        KillingSpreeAura spree = view.GetNode<KillingSpreeAura>("Vfx/KillingSpreeEffect");
        BleedingEffect bleeding = view.GetNode<BleedingEffect>("Vfx/BleedingEffect");
        Assert.True(spree.Active);
        Assert.True(bleeding.Active);

        manager.Place(2, ViewState() with
        {
            Presentation = new PlayerPresentationState
            {
                KillingSpreeMagnitude = 0,
                IsBleeding = true,
            },
        });

        Assert.False(spree.Active);
        Assert.True(bleeding.Active);
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
