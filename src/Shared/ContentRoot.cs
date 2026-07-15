using Godot;

namespace Mortz.Shared;

/// <summary>Where content lives. Everything goes through here: the game, the
/// dedicated server and the tools. Builds read it from a content/ directory
/// next to the executable (tools/export.ps1 puts it there).</summary>
public static class ContentRoot
{
    public static string Resolve()
    {
        string? configured = CmdArgs.GetValue("--content-root");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return OS.HasFeature("editor")
            ? Path.GetFullPath(ProjectSettings.GlobalizePath("res://content"))
            : Path.GetFullPath(Path.Combine(OS.GetExecutablePath().GetBaseDir(), "content"));
    }
}
