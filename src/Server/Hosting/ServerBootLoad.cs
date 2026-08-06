using Mortz.Shared;

namespace Mortz.Server.Hosting;

/// <summary>What the loader produced: the engine-free boot value plus the
/// content the Godot-side map source decodes from.</summary>
public readonly record struct ServerBootLoad(ServerBoot Boot, GameContent Content);
