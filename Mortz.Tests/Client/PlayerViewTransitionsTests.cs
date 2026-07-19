using Godot;
using Mortz.Client.Views;
using Mortz.Core.Replication;
using Xunit;

namespace Mortz.Tests.Client;

public class PlayerViewTransitionsTests
{
    private static PlayerViewState State(byte ammo = 1, byte reload = 0,
        byte health = 100, byte parry = 0, byte dash = 0) =>
        new(Vector2.Zero, 0, 0, ammo, reload, health, 0, parry, dash, 0);

    [Fact]
    public void ReloadCueFiresAtStartAndForEveryFollowingShell()
    {
        PlayerViewTransition started = PlayerViewTransitions.Between(
            State(ammo: 1), State(ammo: 1, reload: 30), isLocal: true);
        PlayerViewTransition nextShell = PlayerViewTransitions.Between(
            State(ammo: 1, reload: 1), State(ammo: 2, reload: 30), isLocal: true);

        Assert.True(started.HasFlag(PlayerViewTransition.SHELL_RELOAD_STARTED));
        Assert.True(nextShell.HasFlag(PlayerViewTransition.SHELL_RELOAD_STARTED));
    }

    [Fact]
    public void ReloadCountdownDoesNotReplayTheCue()
    {
        PlayerViewTransition transition = PlayerViewTransitions.Between(
            State(ammo: 1, reload: 30), State(ammo: 1, reload: 29), isLocal: true);

        Assert.False(transition.HasFlag(PlayerViewTransition.SHELL_RELOAD_STARTED));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void ReloadCompletionInterruptionOrDeathStopsTheCue(byte respawnTicks)
    {
        PlayerViewState previous = State(ammo: 1, reload: 30);
        PlayerViewState next = State(ammo: 1, reload: respawnTicks == 0 ? (byte)0 : (byte)29)
            with
        {
            RespawnTicks = respawnTicks
        };

        PlayerViewTransition transition = PlayerViewTransitions.Between(
            previous, next, isLocal: true);

        Assert.True(transition.HasFlag(PlayerViewTransition.RELOAD_STOPPED));
    }

    [Fact]
    public void LocalDashIgnoresSmallReconciliationCorrections()
    {
        PlayerViewTransition local = PlayerViewTransitions.Between(
            State(dash: 10), State(dash: 12), isLocal: true);
        PlayerViewTransition remote = PlayerViewTransitions.Between(
            State(dash: 10), State(dash: 12), isLocal: false);

        Assert.False(local.HasFlag(PlayerViewTransition.DASHED));
        Assert.True(remote.HasFlag(PlayerViewTransition.DASHED));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(11, false)]
    [InlineData(12, true)]
    public void SpawnImmunityFlicker_Alternates_AndEndsVisible(byte ticks, bool expectedVisible)
    {
        Assert.Equal(expectedVisible, PlayerView.SpawnProtectedSpriteVisible(ticks));
    }

    [Fact]
    public void AuthoritativeOwnShell_ShowsOnlyWhenNoPredictionFlewOrFinished()
    {
        RenderMortar local = new(1, 7, false, 42, default, default);
        RenderMortar remote = local with { OwnerId = 8 };
        RenderMortar deflected = local with { Deflected = true };
        // ReSharper disable once CollectionNeverUpdated.Local
        HashSet<int> none = [];
        HashSet<int> seq42 = [42];

        Assert.False(MortarViewManager.ShouldRenderAuthoritative(local, 7, seq42, none));
        Assert.False(MortarViewManager.ShouldRenderAuthoritative(local, 7, none, seq42));
        Assert.True(MortarViewManager.ShouldRenderAuthoritative(local, 7, none, none));
        Assert.True(MortarViewManager.ShouldRenderAuthoritative(remote, 7, seq42, seq42));
        Assert.True(MortarViewManager.ShouldRenderAuthoritative(deflected, 7, seq42, seq42));
    }
}
