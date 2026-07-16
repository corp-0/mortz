namespace Mortz.Core.Replication;

public sealed record InterpolatedState(IReadOnlyList<RenderPlayer> Players, IReadOnlyList<RenderMortar> Mortars);
