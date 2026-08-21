using System.Collections.Immutable;
using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorCanvasPickerTests
{
    [Fact]
    public void SpawnPickingCoversTheVisibleBody()
    {
        MapEditorSpawn spawn = new(new MapEditorSpawnId(1), new MapSpawnPoint(100, 100));
        MapEditorSnapshot snapshot = Snapshot(spawn);
        MapEditorCanvasPicker picker = new();

        MapEditorSpawn? picked = picker.PickSpawn(snapshot, new Vector2(100, 70), 1f,
            false, Vector2.Zero);

        Assert.Equal(spawn, picked);
    }

    [Fact]
    public void SpawnPickingKeepsAScreenSpaceMarginAroundTheBody()
    {
        MapEditorSpawn spawn = new(new MapEditorSpawnId(1), new MapSpawnPoint(100, 100));
        MapEditorSnapshot snapshot = Snapshot(spawn);
        MapEditorCanvasPicker picker = new();

        Assert.Equal(spawn, picker.PickSpawn(snapshot, new Vector2(82, 80), 1f,
            false, Vector2.Zero));
        Assert.Null(picker.PickSpawn(snapshot, new Vector2(75, 80), 1f,
            false, Vector2.Zero));
    }

    private static MapEditorSnapshot Snapshot(MapEditorSpawn spawn)
    {
        MapEditorLayerAsset layer = new([1], 200, 200);
        return new MapEditorSnapshot("map", "Map", 1, [], [spawn],
            new MapEditorLayers(layer, layer, layer), 200, 200, 0, 0,
            ImmutableArray<ContentDiagnostic>.Empty);
    }
}
