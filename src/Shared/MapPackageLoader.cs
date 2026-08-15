using Godot;
using Mortz.Content;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;

namespace Mortz.Shared;

public sealed record MapPackageLoadResult(
    MapPackage? Package,
    IReadOnlyList<ContentDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.ERROR);
}

/// <summary>Decodes a map's bytes into Godot images and a terrain mask. PNGs
/// are read as raw files (not imported textures) so server and client derive
/// pixel-identical collision. Hands back diagnostics rather than printing them:
/// client, server and tools each want to say it their own way.</summary>
public static class MapPackageLoader
{
    public static MapPackageLoadResult Load(ContentDefinition<MapManifest> definition)
    {
        List<ContentDiagnostic> diagnostics = [];
        ContentReadResult<MapSourceSnapshot> sourceResult = MapSourceSnapshot.Read(definition);
        diagnostics.AddRange(sourceResult.Diagnostics);
        if (sourceResult.Value is not MapSourceSnapshot source)
            return new MapPackageLoadResult(null, diagnostics);

        Image? background = DecodePng(source.BackgroundPng, "background.png", source, diagnostics);
        Image? solid = DecodePng(source.SolidPng, "solid.png", source, diagnostics);
        Image? destructible = DecodePng(source.DestructiblePng, "destructible.png", source, diagnostics);
        if (background == null || solid == null || destructible == null)
            return new MapPackageLoadResult(null, diagnostics);
        if (!SameSize(background, solid) || !SameSize(solid, destructible))
        {
            Error(diagnostics, source.Definition.DirectoryPath,
                "background, solid and destructible layer sizes differ");
            return new MapPackageLoadResult(null, diagnostics);
        }

        diagnostics.AddRange(MapManifestValidator.Validate(source.Manifest,
            source.Definition.ManifestPath,
            new MapDimensions(solid.GetWidth(), solid.GetHeight())));
        if (diagnostics.Any(diagnostic =>
                diagnostic.Severity == ContentDiagnosticSeverity.ERROR))
        {
            return new MapPackageLoadResult(null, diagnostics);
        }

        SpawnPoint[] spawnPoints = source.Manifest.SpawnPoints
            .Select(point => new SpawnPoint(new Vec2(point.X, point.Y), point.Team))
            .ToArray();
        TerrainMask initialTerrain = BuildMask(solid, destructible);

        return new MapPackageLoadResult(new MapPackage
        {
            MapId = definition.Id,
            DisplayName = source.Manifest.Name,
            SuggestedPlayers = source.Manifest.SuggestedPlayers,
            Hash = source.CompatibilityHash,
            Width = solid.GetWidth(),
            Height = solid.GetHeight(),
            SpawnPoints = spawnPoints,
            Zones = MapZoneDefs.Compile(source.Manifest.Zones),
            Background = background,
            Solid = solid,
            Destructible = destructible,
            InitialTerrain = initialTerrain,
        }, diagnostics);
    }

    private static Image? DecodePng(ReadOnlyMemory<byte> bytes, string fileName,
        MapSourceSnapshot source, List<ContentDiagnostic> diagnostics)
    {
        if (bytes.Length == 0)
        {
            Error(diagnostics, source.Definition.DirectoryPath, $"{fileName} is empty");
            return null;
        }
        Image image = new();
        Error error = image.LoadPngFromBuffer(bytes.ToArray());
        if (error == Godot.Error.Ok)
            return image;
        Error(diagnostics, source.Definition.DirectoryPath,
            $"{fileName} is not a valid PNG ({error})");
        return null;
    }

    private static bool SameSize(Image left, Image right) =>
        left.GetWidth() == right.GetWidth() && left.GetHeight() == right.GetHeight();

    private static TerrainMask BuildMask(Image solid, Image destructible) =>
        new(solid.GetWidth(), solid.GetHeight(),
            solid: (x, y) => solid.GetPixel(x, y).A > 0.5f,
            destructible: (x, y) => destructible.GetPixel(x, y).A > 0.5f);

    private static void Error(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message));
}
