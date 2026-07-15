namespace Mortz.Client;

/// <summary>Client-only presentation time. It never changes Godot's global
/// clock or the simulation rate; replay-aware visuals and sounds opt into this
/// scale explicitly.</summary>
internal static class ClientClock
{
    public const float REPLAY_TIME_SCALE = 0.3f;

    public static float TimeScale { get; private set; } = 1f;

    public static void BeginReplay() => TimeScale = REPLAY_TIME_SCALE;

    public static void Reset() => TimeScale = 1f;
}
