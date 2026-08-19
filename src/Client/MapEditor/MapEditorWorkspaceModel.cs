using System.Collections.Immutable;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public enum MapEditorLayer
{
    BACKGROUND,
    SOLID,
    DESTRUCTIBLE,
}

public readonly record struct MapEditorZoneId(long Value);

public readonly record struct MapEditorSpawnId(long Value);

public sealed record MapEditorZone(
    MapEditorZoneId Id,
    string Name,
    ImmutableArray<string> Tags,
    MapZoneShape Shape,
    ImmutableArray<MapZoneEffect> Effects);

public sealed record MapEditorZoneDraft(
    string Name,
    ImmutableArray<string> Tags,
    MapZoneShape Shape,
    ImmutableArray<MapZoneEffect> Effects);

public readonly record struct MapEditorSpawn(
    MapEditorSpawnId Id,
    MapSpawnPoint Value);

public sealed class MapEditorLayerAsset
{
    private readonly byte[] _png;

    public MapEditorLayerAsset(ReadOnlySpan<byte> png, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _png = png.ToArray();
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    // Do not let presentation code recover and mutate the adopted backing array.
    public ReadOnlyMemory<byte> Png => _png.ToArray();
}

public sealed record MapEditorLayers(
    MapEditorLayerAsset Background,
    MapEditorLayerAsset Solid,
    MapEditorLayerAsset Destructible);

public sealed record MapEditorSnapshot(
    string MapId,
    string Name,
    int SuggestedPlayers,
    ImmutableArray<MapEditorZone> Zones,
    ImmutableArray<MapEditorSpawn> SpawnPoints,
    MapEditorLayers Layers,
    int Width,
    int Height,
    long Revision,
    long SavedRevision,
    ImmutableArray<ContentDiagnostic> Diagnostics)
{
    public bool Dirty => Revision != SavedRevision;

    public bool CanSave => Dirty && Diagnostics.All(
        diagnostic => diagnostic.Severity != ContentDiagnosticSeverity.ERROR);
}

public sealed record MapEditorUpdate(
    MapEditorSnapshot Snapshot,
    MapEditorChange Change);

public abstract record MapEditorChange;

public sealed record MapEditorOpened : MapEditorChange;

public sealed record MapEditorReloaded : MapEditorChange;

public sealed record MapEditorSaved : MapEditorChange;

public sealed record MapEditorZoneAdded(MapEditorZoneId Id) : MapEditorChange;

public sealed record MapEditorZoneReplaced(MapEditorZoneId Id) : MapEditorChange;

public sealed record MapEditorZoneRemoved(MapEditorZoneId Id) : MapEditorChange;

public sealed record MapEditorSpawnAdded(MapEditorSpawnId Id) : MapEditorChange;

public sealed record MapEditorSpawnReplaced(MapEditorSpawnId Id) : MapEditorChange;

public sealed record MapEditorSpawnRemoved(MapEditorSpawnId Id) : MapEditorChange;

public sealed record MapEditorLayerReplaced(MapEditorLayer Layer) : MapEditorChange;

public abstract record MapEditorOperationFailure;

public sealed record MapEditorContentFailure(
    ImmutableArray<ContentDiagnostic> Diagnostics) : MapEditorOperationFailure;

public sealed record MapEditorIoFailure(string Message) : MapEditorOperationFailure;

public sealed record MapEditorInvalidPngFailure(string Path, string Reason) : MapEditorOperationFailure;

public sealed record MapEditorLayerSizeFailure(
    int ExpectedWidth,
    int ExpectedHeight,
    int ActualWidth,
    int ActualHeight) : MapEditorOperationFailure;

public sealed record MapEditorOperationResult(
    MapEditorUpdate? Update,
    MapEditorOperationFailure? Failure)
{
    public bool Succeeded => Failure == null;

    public static MapEditorOperationResult Success(MapEditorUpdate update) => new(update, null);

    public static MapEditorOperationResult Failed(MapEditorOperationFailure failure) =>
        new(null, failure);
}

public sealed record MapEditorOpenResult(
    MapEditorWorkspace? Workspace,
    MapEditorUpdate? Update,
    MapEditorOperationFailure? Failure)
{
    public bool Succeeded => Workspace != null && Update != null;
}
