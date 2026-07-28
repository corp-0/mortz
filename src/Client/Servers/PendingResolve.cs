using Mortz.Core.Net.Query;

namespace Mortz.Client.Servers;

/// <summary>A hostname probe parked in Godot's resolver queue.</summary>
public readonly record struct PendingResolve(int ResolveId, ServerEndpoint Endpoint);
