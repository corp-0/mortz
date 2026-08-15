using System.Collections.Immutable;
using Godot;
using Mortz.Core.Replication;
using Mortz.Core.Sim;

namespace Mortz.Client.Views;

/// <summary>Samples live interpolation and prediction without touching scene nodes.</summary>
public sealed class LiveMatchFrameBuilder(Func<int, byte, byte> sampleSkin)
{
    public PresentedMatchFrame Build(
        float tick,
        InterpolatedState remoteState,
        int localId,
        PlayerState? localState,
        Vector2 localCorrection,
        byte localAim,
        IReadOnlyList<RenderMortar> authoritativeMortars,
        IReadOnlyList<(int SpawnSeq, MortarState Shell)> predictedMortars,
        IReadOnlySet<int> completedPredictedMortars)
    {
        ImmutableArray<PresentedPlayer>.Builder players = ImmutableArray.CreateBuilder<PresentedPlayer>();
        ImmutableArray<RopeSegment>.Builder ropes = ImmutableArray.CreateBuilder<RopeSegment>();
        PlayerPresentationState localPresentation = default;

        foreach (RenderPlayer player in remoteState.Players)
        {
            if (player.PeerId == localId)
            {
                localPresentation = player.Presentation;
                continue;
            }

            PlayerViewState viewState = ViewState(player, sampleSkin(player.PeerId, player.Skin));
            players.Add(new PresentedPlayer(player.PeerId, viewState));
            if (player.Rope != RopeMode.NONE)
            {
                ropes.Add(new RopeSegment(
                    BodyCenter(player.Position),
                    new Vector2(player.RopePoint.X, player.RopePoint.Y)));
            }
        }

        if (localState is PlayerState local)
        {
            Vector2 feet = new(local.Position.X, local.Position.Y);
            PlayerViewState viewState = new(
                feet + localCorrection,
                localAim,
                sampleSkin(localId, local.Skin),
                local.Ammo,
                local.ReloadTicks,
                local.Health,
                local.RespawnTicks,
                local.ParryTicks,
                local.DashCooldown,
                local.SpawnImmunityTicks,
                localPresentation);
            players.Add(new PresentedPlayer(localId, viewState));
            if (local.Rope != RopeMode.NONE)
            {
                ropes.Add(new RopeSegment(
                    BodyCenter(local.Position) + localCorrection,
                    new Vector2(local.RopePoint.X, local.RopePoint.Y)));
            }
        }

        ImmutableArray<PresentedMortar>.Builder mortars =
            ImmutableArray.CreateBuilder<PresentedMortar>();
        HashSet<int> predictedSeqs = predictedMortars
            .Select(entry => entry.SpawnSeq)
            .ToHashSet();
        foreach (RenderMortar mortar in authoritativeMortars)
        {
            if (!ShouldRenderAuthoritative(
                    mortar, localId, predictedSeqs, completedPredictedMortars))
                continue;
            mortars.Add(new PresentedMortar(
                PresentedMortarKey.Authoritative(mortar.Id),
                new Vector2(mortar.Position.X, mortar.Position.Y),
                mortar.Velocity));
        }

        foreach ((int spawnSeq, MortarState shell) in predictedMortars)
        {
            mortars.Add(new PresentedMortar(
                PresentedMortarKey.Predicted(spawnSeq),
                new Vector2(shell.Position.X, shell.Position.Y),
                shell.Velocity));
        }

        return new PresentedMatchFrame(
            tick,
            players.ToImmutable(),
            mortars.ToImmutable(),
            ropes.ToImmutable());
    }

    public static bool ShouldRenderAuthoritative(
        in RenderMortar mortar,
        int localId,
        IReadOnlySet<int> predictedSeqs,
        IReadOnlySet<int> completedSeqs) =>
        mortar.OwnerId != localId || mortar.Deflected ||
        (!predictedSeqs.Contains(mortar.SpawnSeq) &&
         !completedSeqs.Contains(mortar.SpawnSeq));

    private static PlayerViewState ViewState(in RenderPlayer player, byte skin) => new(
        new Vector2(player.Position.X, player.Position.Y),
        player.Aim,
        skin,
        player.Ammo,
        player.ReloadTicks,
        player.Health,
        player.RespawnTicks,
        player.ParryTicks,
        player.DashCooldown,
        player.SpawnImmunityTicks,
        player.Presentation);

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
