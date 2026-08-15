using System.Collections.Immutable;
using Mortz.Client.Views;
using Mortz.Core.Sim;

namespace Mortz.Client.Replay;

public sealed class ReplayClip
{
    private readonly PresentedMatchFrame[] _frames;

    public float StartTick => _frames[0].Tick;
    public float EndTick => _frames[^1].Tick;

    public ReplayClip(PresentedMatchFrame[] frames) => _frames = frames;

    public PresentedMatchFrame Sample(float tick)
    {
        if (tick <= StartTick)
            return _frames[0];
        if (tick >= EndTick)
            return _frames[^1];

        int newerIndex = Array.FindIndex(_frames, frame => frame.Tick >= tick);
        PresentedMatchFrame older = _frames[newerIndex - 1];
        PresentedMatchFrame newer = _frames[newerIndex];
        float span = newer.Tick - older.Tick;
        float amount = span > 0 ? (tick - older.Tick) / span : 1f;
        return Interpolate(older, newer, amount, tick);
    }

    private static PresentedMatchFrame Interpolate(
        PresentedMatchFrame older, PresentedMatchFrame newer, float amount, float tick)
    {
        Dictionary<int, PresentedPlayer> oldPlayers = older.Players
            .ToDictionary(player => player.PeerId);
        ImmutableArray<PresentedPlayer> players = newer.Players.Select(player =>
        {
            if (!oldPlayers.TryGetValue(player.PeerId, out PresentedPlayer previous))
                return player;
            PlayerViewState state = player.State with
            {
                Feet = previous.State.Feet.Lerp(player.State.Feet, amount),
            };
            return new PresentedPlayer(player.PeerId, state);
        }).ToImmutableArray();

        Dictionary<PresentedMortarKey, PresentedMortar> oldMortars = older.Mortars
            .ToDictionary(mortar => mortar.Key);
        ImmutableArray<PresentedMortar> mortars = newer.Mortars.Select(mortar =>
        {
            if (!oldMortars.TryGetValue(mortar.Key, out PresentedMortar previous))
                return mortar;
            return mortar with
            {
                Position = previous.Position.Lerp(mortar.Position, amount),
                Velocity = Vec2.Lerp(previous.Velocity, mortar.Velocity, amount),
            };
        }).ToImmutableArray();

        return new PresentedMatchFrame(tick, players, mortars, newer.Ropes);
    }
}
