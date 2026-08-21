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
    private readonly ReadOnlyMemory<byte> _png;

    public MapEditorLayerAsset(ReadOnlySpan<byte> png, int width, int height)
        : this((ReadOnlyMemory<byte>)png.ToArray(), width, height)
    {
    }

    private MapEditorLayerAsset(ReadOnlyMemory<byte> png, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (png.IsEmpty)
            throw new ArgumentException("PNG data is required.", nameof(png));

        _png = png;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public ReadOnlyMemory<byte> Png => _png;

    // The caller gives up mutable access to the buffer after this call.
    internal static MapEditorLayerAsset AdoptOwnedPng(ReadOnlyMemory<byte> png,
        int width, int height) => new(png, width, height);
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
    ImmutableArray<ContentDiagnostic> Diagnostics,
    MapEditorRasterSourceStatus SourceStatus = MapEditorRasterSourceStatus.OBSOLETE,
    MapEditorBrushDocument? BrushDocument = null,
    bool CanUndo = false,
    bool CanRedo = false,
    long StateId = -1,
    long SavedStateId = -1,
    long OriginX = 0,
    long OriginY = 0,
    MapEditorMapBounds? FittedBounds = null)
{
    public MapEditorMapBounds Bounds => FittedBounds ?? new(OriginX, OriginY, Width, Height);

    public bool Dirty => StateId >= 0
        ? StateId != SavedStateId
        : Revision != SavedRevision;

    public bool CanSave =>
        Dirty && Diagnostics.All(diagnostic => diagnostic.Severity != ContentDiagnosticSeverity.ERROR);

    public bool CanEditBrushes => SourceStatus == MapEditorRasterSourceStatus.BRUSH_SOURCE;
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

public sealed record MapEditorBrushSourceInitialized : MapEditorChange;

public sealed record MapEditorBrushAdded(MapEditorBrushId Id) : MapEditorChange;

public sealed record MapEditorBrushesAdded(
    ImmutableArray<MapEditorBrushId> Ids) : MapEditorChange;

public sealed record MapEditorBrushReplaced(MapEditorBrushId Id) : MapEditorChange;

public sealed record MapEditorBrushRemoved(MapEditorBrushId Id) : MapEditorChange;

public sealed record MapEditorBrushesRemoved(
    ImmutableArray<MapEditorBrushId> Ids) : MapEditorChange;

public sealed record MapEditorBrushReordered(MapEditorBrushId Id) : MapEditorChange;

public sealed record MapEditorBrushMovedToLayer(
    MapEditorBrushId Id,
    MapEditorLayer From,
    MapEditorLayer To) : MapEditorChange;

public sealed record MapEditorStampSaved(MapEditorStampId Id) : MapEditorChange;

public sealed record MapEditorStampRemoved(MapEditorStampId Id) : MapEditorChange;

public sealed record MapEditorUndoApplied(MapEditorChange OriginalChange) : MapEditorChange;

public sealed record MapEditorRedoApplied(MapEditorChange OriginalChange) : MapEditorChange;

public abstract record MapEditorOperationFailure;

public sealed record MapEditorContentFailure(
    ImmutableArray<ContentDiagnostic> Diagnostics) : MapEditorOperationFailure;

public sealed record MapEditorIoFailure(string Message) : MapEditorOperationFailure;

public sealed record MapEditorBrushEditingUnavailableFailure : MapEditorOperationFailure;

public sealed record MapEditorUnresolvedBrushesFailure(
    ImmutableArray<MapEditorUnresolvedBrush> Brushes) : MapEditorOperationFailure;

public sealed record MapEditorCompositionFailure(
    MapEditorLayer Layer,
    string Message) : MapEditorOperationFailure;

public sealed record MapEditorIdentityOverflowFailure(string ObjectType) :
    MapEditorOperationFailure;

public sealed record MapEditorOperationResult(
    MapEditorUpdate? Update,
    MapEditorOperationFailure? Failure)
{
    public bool Succeeded => Failure == null;

    public static MapEditorOperationResult Success(MapEditorUpdate update) => new(update, null);

    public static MapEditorOperationResult Failed(MapEditorOperationFailure failure) =>
        new(null, failure);
}

public sealed record MapEditorRuntimeManifestResult(
    MapManifest? Manifest,
    MapEditorOperationFailure? Failure)
{
    public bool Succeeded => Manifest != null;
}

public sealed record MapEditorOpenResult(
    MapEditorWorkspace? Workspace,
    MapEditorUpdate? Update,
    MapEditorOperationFailure? Failure)
{
    public bool Succeeded => Workspace != null && Update != null;
}
