using Godot;

namespace Mortz.Shared;

/// <summary>True for exported server builds or dev runs started with --server.</summary>
public static class RunMode
{
    public static bool IsDedicatedServer =>
        OS.HasFeature("dedicated_server") || CmdArgs.HasFlag("--server");
}
