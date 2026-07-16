using Godot;
using Mortz.Core;
using Mortz.Core.Sim;

namespace Mortz.Client;

internal readonly record struct ReplayPlayer(int PeerId, PlayerViewState State);

internal readonly record struct ReplayMortar(
    long Key, Vector2 Position, Vec2 Velocity);

internal sealed record ReplayFrame(
    float Tick,
    ReplayPlayer[] Players,
    ReplayMortar[] Mortars,
    (Vector2 From, Vector2 To)[] Ropes);

/// <summary>A short rolling copy of exactly what was presented on screen. It
/// is detached from prediction and terrain, so sampling it cannot affect the
/// authoritative client state.</summary>
internal sealed class ReplayHistory
{
    internal const float HISTORY_TICKS = SimConfig.TICK_RATE;
    internal const float REPLAY_TICKS = SimConfig.TICK_RATE * 0.75f;

    private readonly List<ReplayFrame> _frames = new();

    public void Add(ReplayFrame frame)
    {
        _frames.Add(frame);
        float oldest = frame.Tick - HISTORY_TICKS;
        int remove = _frames.FindIndex(f => f.Tick >= oldest);
        if (remove > 0)
            _frames.RemoveRange(0, remove);
    }

    public ReplayClip? Capture(int eventTick)
    {
        if (_frames.Count < 2)
            return null;
        float end = Math.Min(eventTick, _frames[^1].Tick);
        int last = _frames.FindLastIndex(frame => frame.Tick <= end);
        if (last < 1)
            return null;
        float startTick = end - REPLAY_TICKS;
        int first = _frames.FindLastIndex(last, last + 1, frame => frame.Tick <= startTick);
        first = Math.Max(0, first);
        ReplayFrame[] frames = _frames.GetRange(first, last - first + 1).ToArray();
        if (frames[^1].Tick - frames[0].Tick < SimConfig.TICK_RATE * 0.25f)
            return null;
        return new ReplayClip(frames);
    }
}

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
