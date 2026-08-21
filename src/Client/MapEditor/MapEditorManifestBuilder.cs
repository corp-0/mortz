using System.Collections.Immutable;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public static class MapEditorManifestBuilder
{
    public static MapEditorRuntimeManifestResult BuildRuntime(string name, int suggestedPlayers,
        ImmutableArray<MapEditorZone> zones, ImmutableArray<MapEditorSpawn> spawns,
        MapEditorBrushDocument? brushDocument, long originX, long originY,
        long boundsWidth, long boundsHeight, int width, int height, string validationSource)
    {
        if (brushDocument != null &&
            (boundsWidth > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION ||
             boundsHeight > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION))
        {
            return Failure(validationSource,
                $"The map is {boundsWidth} x {boundsHeight}. The maximum size is " +
                $"{MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION} x " +
                $"{MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION}.");
        }
        MapManifest manifest;
        try
        {
            manifest = BuildTranslated(name, suggestedPlayers, zones, spawns, originX, originY);
        }
        catch (OverflowException)
        {
            return Failure(validationSource,
                "Some objects are too far from the map to save.");
        }
        if (brushDocument == null)
            return new MapEditorRuntimeManifestResult(manifest, null);
        ImmutableArray<ContentDiagnostic> diagnostics = MapManifestValidator.Validate(
                manifest, validationSource, new MapDimensions(width, height))
            .Where(diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
            .ToImmutableArray();
        return diagnostics.IsEmpty
            ? new MapEditorRuntimeManifestResult(manifest, null)
            : new MapEditorRuntimeManifestResult(null, new MapEditorContentFailure(diagnostics));
    }

    public static MapManifest BuildTranslated(string name, int suggestedPlayers,
        ImmutableArray<MapEditorZone> zones, ImmutableArray<MapEditorSpawn> spawns,
        long originX, long originY) => new()
        {
            Name = name,
            SuggestedPlayers = suggestedPlayers,
            Zones = zones.Select(zone => new MapZoneDef
            {
                Name = zone.Name,
                Tags = [.. zone.Tags],
                Shape = MapEditorMapBoundsFitter.Translate(zone, -originX, -originY).Shape,
                Effects = [.. zone.Effects],
            }).ToArray(),
            SpawnPoints = spawns.Select(spawn =>
                MapEditorMapBoundsFitter.Translate(spawn.Value, -originX, -originY)).ToArray(),
        };

    public static MapManifest BuildAuthoring(string name, int suggestedPlayers,
        ImmutableArray<MapEditorZone> zones, ImmutableArray<MapEditorSpawn> spawns) => new()
        {
            Name = name,
            SuggestedPlayers = suggestedPlayers,
            Zones = zones.Select(zone => new MapZoneDef
            {
                Name = zone.Name,
                Tags = [.. zone.Tags],
                Shape = zone.Shape,
                Effects = [.. zone.Effects],
            }).ToArray(),
            SpawnPoints = spawns.Select(spawn => spawn.Value).ToArray(),
        };

    private static MapEditorRuntimeManifestResult Failure(string source, string message) =>
        new(null, new MapEditorContentFailure(
            [new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message)]));
}
