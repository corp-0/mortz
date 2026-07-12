using System.Text.Json;
using Godot;
using Mortz.Core;
using FileAccess = Godot.FileAccess;

namespace Mortz.Shared;

/// <summary>
/// A loaded map: three PNG layers + manifest, from res://maps/&lt;id&gt;/.
/// Background never collides; Solid collides and is indestructible;
/// Destructible collides and is carvable. PNGs are read as raw files (not
/// imported textures) so both server and client derive pixel-identical
/// collision masks; the hash lets the server reject mismatched map files.
/// </summary>
public sealed class MapPackage
{
    public required string MapId { get; init; }
    public required string DisplayName { get; init; }
    public required int SuggestedPlayers { get; init; }
    public required string Hash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Image Background { get; init; }
    public required Image Solid { get; init; }
    public required Image Destructible { get; init; }

    // All content access goes through here. When the content manager arrives
    // (multiple packs, external mod dirs), only this root changes.
    // Content is not embedded in the export; builds read it from a content/
    // directory next to the executable (tools/export.ps1 puts it there).
    private static string ContentRoot => OS.HasFeature("editor")
        ? "res://content/Base"
        : OS.GetExecutablePath().GetBaseDir().PathJoin("content/Base");

    public static MapPackage? Load(string mapId)
    {
        string dir = $"{ContentRoot}/maps/{mapId}";
        Image? background = LoadPng($"{dir}/background.png");
        Image? solid = LoadPng($"{dir}/solid.png");
        Image? destructible = LoadPng($"{dir}/destructible.png");
        string manifestText = FileAccess.GetFileAsString($"{dir}/map.json");
        if (background == null || solid == null || destructible == null || string.IsNullOrEmpty(manifestText))
        {
            GD.PrintErr($"[map] missing files in {dir}");
            return null;
        }
        if (solid.GetWidth() != destructible.GetWidth() || solid.GetHeight() != destructible.GetHeight())
        {
            GD.PrintErr($"[map] {mapId}: solid and destructible layer sizes differ");
            return null;
        }

        using JsonDocument manifest = JsonDocument.Parse(manifestText);
        JsonElement root = manifest.RootElement;

        string hash = string.Join(":",
            FileAccess.GetMd5($"{dir}/background.png"),
            FileAccess.GetMd5($"{dir}/solid.png"),
            FileAccess.GetMd5($"{dir}/destructible.png"),
            FileAccess.GetMd5($"{dir}/map.json"));

        return new MapPackage
        {
            MapId = mapId,
            DisplayName = root.GetProperty("name").GetString() ?? mapId,
            SuggestedPlayers = root.TryGetProperty("suggestedPlayers", out JsonElement sp) ? sp.GetInt32() : NetConfig.MAX_PLAYERS,
            Hash = hash,
            Width = solid.GetWidth(),
            Height = solid.GetHeight(),
            Background = background,
            Solid = solid,
            Destructible = destructible,
        };
    }

    public TerrainMask BuildMask() => new(Width, Height,
        solid: (x, y) => Solid.GetPixel(x, y).A > 0.5f,
        destructible: (x, y) => Destructible.GetPixel(x, y).A > 0.5f);

    private static Image? LoadPng(string path)
    {
        byte[] bytes = FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
            return null;
        Image image = new Image();
        return image.LoadPngFromBuffer(bytes) == Error.Ok ? image : null;
    }
}
