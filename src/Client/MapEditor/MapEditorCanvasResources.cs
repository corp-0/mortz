using Godot;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorCanvasResources : IDisposable
{
    private readonly IMapEditorTextureResolver _resolver;
    private readonly Dictionary<MapEditorTextureReference, MapEditorTextureResolution> _resolutions = [];
    private readonly Dictionary<MapEditorBrushMaterial, ImageTexture> _previewTextures = [];
    private readonly MapEditorTextureData _missingPreview = MapEditorMissingTexturePreview.Create();

    private readonly Dictionary<MapEditorLayer,
        (MapEditorBrushMaterial Material, MapEditorTextureProjection Projection)> _lastMaterials = [];

    private ImageTexture? _missingPreviewTexture;
    private ImageTexture? _background;
    private ImageTexture? _solid;
    private ImageTexture? _destructible;

    public MapEditorCanvasResources(IMapEditorTextureResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public ImageTexture? BakedTexture(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => _background,
        MapEditorLayer.SOLID => _solid,
        MapEditorLayer.DESTRUCTIBLE => _destructible,
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    public MapEditorTextureResolution Resolve(MapEditorTextureReference reference)
    {
        if (_resolutions.TryGetValue(reference, out MapEditorTextureResolution? cached))
            return cached;
        MapEditorTextureResolution resolution;
        try
        {
            resolution = _resolver.Resolve(reference);
        }
        catch (Exception exception)
        {
            resolution = new MapEditorTextureResolution(
                MapEditorTextureResolutionStatus.LOAD_ERROR, reference, null,
                $"Couldn't preview texture: {exception.Message}");
        }

        _resolutions.Add(reference, resolution);
        return resolution;
    }

    public (MapEditorTextureData Data, ImageTexture Texture, bool Missing) Preview(
        MapEditorBrushMaterial material)
    {
        if (material is MapEditorSolidColorMaterial solid)
        {
            MapEditorTextureData solidData = MapEditorTextureData.Solid(solid.Color);
            if (_previewTextures.TryGetValue(material, out ImageTexture? solidTexture))
                return (solidData, solidTexture, false);
            solidTexture = CreateTexture(solidData);
            _previewTextures.Add(material, solidTexture);
            return (solidData, solidTexture, false);
        }

        if (material is not MapEditorTextureMaterial textureMaterial)
            throw new ArgumentOutOfRangeException(nameof(material));
        MapEditorTextureReference reference = textureMaterial.Reference;
        MapEditorTextureResolution resolution = Resolve(reference);
        bool missing = !resolution.IsResolved;
        MapEditorTextureData data = missing ? _missingPreview : resolution.Texture!;
        if (missing)
            return (data, _missingPreviewTexture ??= CreateTexture(data), true);
        if (_previewTextures.TryGetValue(material, out ImageTexture? cached))
            return (data, cached, false);
        ImageTexture texture = CreateTexture(data);
        _previewTextures.Add(material, texture);
        return (data, texture, false);
    }

    public void RefreshBakedTextures(MapEditorSnapshot snapshot, MapEditorLayer selectedLayer,
        bool showBackground, bool showSolid, bool showDestructible,
        IReadOnlySet<MapEditorLayer>? changedLayers)
    {
        Refresh(MapEditorLayer.BACKGROUND, showBackground, ref _background);
        Refresh(MapEditorLayer.SOLID, showSolid, ref _solid);
        Refresh(MapEditorLayer.DESTRUCTIBLE, showDestructible, ref _destructible);
        return;

        void Refresh(MapEditorLayer layer, bool visible, ref ImageTexture? texture)
        {
            bool sourcePreview = snapshot.BrushDocument is { } document &&
                (selectedLayer == layer || document.Layers.Get(layer).BakeDirty);
            bool retain = visible && !sourcePreview;
            if (!retain || changedLayers?.Contains(layer) == true)
            {
                texture?.Dispose();
                texture = null;
            }

            if (!retain || texture != null)
                return;
            texture = DecodeTexture(snapshot.Layers, layer);
        }
    }

    public void ClearMaterials() => _lastMaterials.Clear();

    public void RememberMaterial(MapEditorBrush brush)
    {
        if (brush.Material is MapEditorSolidColorMaterial ||
            brush.Material is MapEditorTextureMaterial texture && Resolve(texture.Reference).IsResolved)
        {
            _lastMaterials[brush.Layer] = (brush.Material, brush.Projection);
        }
    }

    public bool TryGetMaterial(MapEditorLayer layer,
        out (MapEditorBrushMaterial Material, MapEditorTextureProjection Projection) material) =>
        _lastMaterials.TryGetValue(layer, out material);

    public void InvalidatePreviews()
    {
        foreach (ImageTexture texture in _previewTextures.Values)
        {
            texture.Dispose();
        }

        _previewTextures.Clear();
        _missingPreviewTexture?.Dispose();
        _missingPreviewTexture = null;
        _resolutions.Clear();
        _resolver.Invalidate();
    }

    public void Dispose()
    {
        InvalidatePreviews();
        _background?.Dispose();
        _solid?.Dispose();
        _destructible?.Dispose();
        _background = null;
        _solid = null;
        _destructible = null;
    }

    public static HashSet<MapEditorLayer> ChangedBakedLayers(MapEditorSnapshot? previous,
        MapEditorSnapshot current)
    {
        HashSet<MapEditorLayer> changed = [];
        if (previous == null || !SameAsset(previous.Layers.Background, current.Layers.Background))
            changed.Add(MapEditorLayer.BACKGROUND);
        if (previous == null || !SameAsset(previous.Layers.Solid, current.Layers.Solid))
            changed.Add(MapEditorLayer.SOLID);
        if (previous == null || !SameAsset(previous.Layers.Destructible,
                current.Layers.Destructible))
            changed.Add(MapEditorLayer.DESTRUCTIBLE);
        return changed;
    }

    private static bool SameAsset(MapEditorLayerAsset first, MapEditorLayerAsset second) =>
        ReferenceEquals(first, second) ||
        first.Width == second.Width && first.Height == second.Height &&
        first.Png.Span.SequenceEqual(second.Png.Span);

    private static ImageTexture CreateTexture(MapEditorTextureData data)
    {
        using Image image = Image.CreateFromData(data.Width, data.Height, false,
            Image.Format.Rgba8, data.Rgba.ToArray());
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture DecodeTexture(MapEditorLayers layers, MapEditorLayer layer)
    {
        MapEditorLayerAsset asset = layer switch
        {
            MapEditorLayer.BACKGROUND => layers.Background,
            MapEditorLayer.SOLID => layers.Solid,
            MapEditorLayer.DESTRUCTIBLE => layers.Destructible,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
        using Image image = new();
        Error error = image.LoadPngFromBuffer(asset.Png.ToArray());
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not decode adopted map layer ({error}).");
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);
        return ImageTexture.CreateFromImage(image);
    }
}
