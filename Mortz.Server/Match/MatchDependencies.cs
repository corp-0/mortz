using Mortz.Server.Diagnostics;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Mortz.Server.Wins;
using Serilog;

namespace Mortz.Server.Match;

/// <summary>Stable server dependencies used to compose each match.</summary>
public sealed class MatchDependencies
{
    public required SettingsService Settings { get; init; }

    public required WinsService Wins { get; init; }

    public required Roster Roster { get; init; }

    public required IServerLink Link { get; init; }

    public required ILogger Log { get; init; }

    public required IMatchObserver Observer { get; init; }

    public required IMatchControl Control { get; init; }

    public required ServerClock Clock { get; init; }

    public required bool NetStats { get; init; }

    public required bool AllowJoinInProgress { get; init; }
}
