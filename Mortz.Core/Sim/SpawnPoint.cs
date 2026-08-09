using Mortz.Core.Match.Teams;

namespace Mortz.Core.Sim;

public readonly record struct SpawnPoint(Vec2 Position, Team? Team = null);
