using Mortz.Client.Views;
using Mortz.Core.Sim;

namespace Mortz.Client.Replay;

internal sealed class ReplayClip
{
    private readonly ReplayFrame[] _frames;

    public float StartTick => _frames[0].Tick;
    public float EndTick => _frames[^1].Tick;

    public ReplayClip(ReplayFrame[] frames) => _frames = frames;

    public ReplayFrame Sample(float tick)
    {
        if (tick <= StartTick)
            return _frames[0];
        if (tick >= EndTick)
            return _frames[^1];

        int newerIndex = Array.FindIndex(_frames, frame => frame.Tick >= tick);
        ReplayFrame older = _frames[newerIndex - 1];
        ReplayFrame newer = _frames[newerIndex];
        float span = newer.Tick - older.Tick;
        float amount = span > 0 ? (tick - older.Tick) / span : 1f;
        return Interpolate(older, newer, amount, tick);
    }

    private static ReplayFrame Interpolate(
        ReplayFrame older, ReplayFrame newer, float amount, float tick)
    {
        Dictionary<int, ReplayPlayer> oldPlayers = older.Players
            .ToDictionary(player => player.PeerId);
        ReplayPlayer[] players = newer.Players.Select(player =>
        {
            if (!oldPlayers.TryGetValue(player.PeerId, out ReplayPlayer previous))
                return player;
            PlayerViewState state = player.State with
            {
                Feet = previous.State.Feet.Lerp(player.State.Feet, amount),
            };
            return new ReplayPlayer(player.PeerId, state);
        }).ToArray();

        Dictionary<long, ReplayMortar> oldMortars = older.Mortars
            .ToDictionary(mortar => mortar.Key);
        ReplayMortar[] mortars = newer.Mortars.Select(mortar =>
        {
            if (!oldMortars.TryGetValue(mortar.Key, out ReplayMortar previous))
                return mortar;
            return mortar with
            {
                Position = previous.Position.Lerp(mortar.Position, amount),
                Velocity = Vec2.Lerp(previous.Velocity, mortar.Velocity, amount),
            };
        }).ToArray();

        return new ReplayFrame(tick, players, mortars, newer.Ropes);
    }
}
