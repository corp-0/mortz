namespace Mortz.Client.MapEditor;

public class MapEditorCanvasDraftFactory(MapEditorCanvasResources resources)
{
    public string UniqueZoneName(MapEditorSnapshot snapshot)
    {
        HashSet<string> names = snapshot.Zones.Select(zone => zone.Name).ToHashSet();
        for (int number = 1; ; number++)
        {
            string name = $"zone-{number}";
            if (!names.Contains(name))
                return name;
        }
    }

    public string UniqueBrushName(MapEditorSnapshot? snapshot, MapEditorLayer layer,
        string shape = "rectangle")
    {
        HashSet<string> names = snapshot?.BrushDocument?.Layers.Get(layer).Brushes
            .Select(brush => brush.Name).ToHashSet() ?? [];
        for (int number = 1; ; number++)
        {
            string name = $"{shape}-{number}";
            if (!names.Contains(name))
                return name;
        }
    }

    public MapEditorBrushDraft CreateBrush(string name, MapEditorLayer layer,
        MapEditorBrushShape shape, MapEditorPoint anchor)
    {
        if (resources.TryGetMaterial(layer,
                out (MapEditorBrushMaterial Material,
                    MapEditorTextureProjection Projection) material))
        {
            return new MapEditorBrushDraft(name, layer, shape, material.Material,
                material.Projection with { Origin = anchor });
        }
        return new MapEditorBrushDraft(name, layer, shape,
            new MapEditorSolidColorMaterial(new MapEditorColor(255, 255, 255)),
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT, anchor, 1, 1, 0));
    }
}
