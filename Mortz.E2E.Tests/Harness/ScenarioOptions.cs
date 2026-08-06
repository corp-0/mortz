namespace Mortz.E2E.Tests.Harness;

/// <summary>What one scenario asks of the processes it starts. No seed: the
/// boot seed reaches only skins and chat, and no scenario asserts on either.</summary>
public sealed record ScenarioOptions
{
    public required string Name { get; init; }

    /// <summary>The cast, launched and brought into the lobby by StartAsync.
    /// Empty for a server-only scenario. Required so every scenario states who
    /// is playing rather than inheriting someone else's default.</summary>
    public required IReadOnlyList<string> Players { get; init; }

    public string MapId { get; init; } = "flat";

    /// <summary>Mutually exclusive with RulesetPath, like the server's own flags.</summary>
    public string? Mode { get; init; } = "deathmatch";

    public string? RulesetPath { get; init; }

    /// <summary>Clamped to [1, 4] by the game; loopback only. Measured inert on
    /// Godot 4.7.1: Engine.TimeScale does not change the physics tick rate, so
    /// the default is 1 until a mechanism that really compresses time exists.</summary>
    public double Timescale { get; init; } = 1;

    /// <summary>Bind the browser query responder only when a scenario needs it.</summary>
    public int? QueryPort { get; init; }

    /// <summary>Render the clients so a human can watch the scenario run.
    /// Off by default and forced off in CI. The server stays headless either
    /// way: it draws nothing.</summary>
    public bool Windowed { get; init; }

    /// <summary>Simulates a player with shitty computer.</summary>
    public string? SlowScreenPlayer { get; init; }

    public int ScreenLoadDelayMs { get; init; } = 4_000;
}
