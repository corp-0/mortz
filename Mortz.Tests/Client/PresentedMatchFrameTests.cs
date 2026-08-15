using Godot;
using Mortz.Client.Views;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client;

public class PresentedMatchFrameTests
{
    [Fact]
    public void BuilderSamplesRemoteInterpolationLocalPredictionAndHistoricalPresentation()
    {
        const int LOCAL_ID = 7;
        LiveMatchFrameBuilder builder = new((peerId, _) =>
            peerId == LOCAL_ID ? (byte)3 : (byte)4);
        InterpolatedState remote = new([
            Player(LOCAL_ID, x: -100, RopeMode.NONE, magnitude: 9),
            Player(8, x: 20, RopeMode.ATTACHED, magnitude: 5),
        ]);
        PlayerState local = new()
        {
            PeerId = LOCAL_ID,
            Position = new Vec2(100, 200),
            Rope = RopeMode.ATTACHED,
            RopePoint = new Vec2(120, 140),
            Ammo = 2,
            Health = 80,
        };

        PresentedMatchFrame frame = builder.Build(
            42.5f,
            remote,
            LOCAL_ID,
            local,
            new Vector2(3, -2),
            localAim: 11,
            authoritativeMortars: [],
            predictedMortars: [],
            completedPredictedMortars: new HashSet<int>());

        PresentedPlayer localPlayer = Assert.Single(
            frame.Players.Where(player => player.PeerId == LOCAL_ID));
        PresentedPlayer remotePlayer = Assert.Single(
            frame.Players.Where(player => player.PeerId == 8));
        Assert.Equal(new Vector2(103, 198), localPlayer.State.Feet);
        Assert.Equal(new Vector2(20, 200), remotePlayer.State.Feet);
        Assert.Equal(9, localPlayer.State.Presentation.KillingSpreeMagnitude);
        Assert.Equal(3, localPlayer.State.Skin);
        Assert.Equal(4, remotePlayer.State.Skin);
        Assert.Equal(2, frame.Ropes.Length);
    }

    [Fact]
    public void BuilderDeduplicatesLocalAuthoritativeMortarsWithTypedKeys()
    {
        const int LOCAL_ID = 7;
        LiveMatchFrameBuilder builder = new((_, fallback) => fallback);
        RenderMortar local = Mortar(id: 1, owner: LOCAL_ID, spawnSeq: 42);
        RenderMortar remote = Mortar(id: 2, owner: 8, spawnSeq: 42);
        RenderMortar deflected = Mortar(id: 3, owner: LOCAL_ID, spawnSeq: 42) with
        {
            Deflected = true,
        };
        MortarState predicted = new()
        {
            SpawnSeq = 42,
            Position = new Vec2(9, 10),
            Velocity = new Vec2(1, 2),
        };

        PresentedMatchFrame frame = builder.Build(
            10,
            new InterpolatedState([]),
            LOCAL_ID,
            localState: null,
            Vector2.Zero,
            localAim: 0,
            authoritativeMortars: [local, remote, deflected],
            predictedMortars: [(42, predicted)],
            completedPredictedMortars: new HashSet<int>());

        Assert.DoesNotContain(frame.Mortars,
            mortar => mortar.Key == PresentedMortarKey.Authoritative(1));
        Assert.Contains(frame.Mortars,
            mortar => mortar.Key == PresentedMortarKey.Authoritative(2));
        Assert.Contains(frame.Mortars,
            mortar => mortar.Key == PresentedMortarKey.Authoritative(3));
        Assert.Contains(frame.Mortars,
            mortar => mortar.Key == PresentedMortarKey.Predicted(42));
        Assert.Equal(3, frame.Mortars.Length);
    }

    private static RenderPlayer Player(
        int peerId,
        float x,
        RopeMode rope,
        byte magnitude) => new(
        peerId,
        new Vec2(x, 200),
        Aim: 1,
        Skin: 0,
        rope,
        new Vec2(x + 10, 100),
        Ammo: 3,
        ReloadTicks: 0,
        Health: 100,
        RespawnTicks: 0,
        SpawnImmunityTicks: 0,
        ParryTicks: 0,
        DashCooldown: 0,
        new PlayerPresentationState { KillingSpreeMagnitude = magnitude });

    private static RenderMortar Mortar(ushort id, int owner, int spawnSeq) => new(
        id,
        owner,
        Deflected: false,
        spawnSeq,
        new Vec2(id, id),
        new Vec2(1, 1));
}
