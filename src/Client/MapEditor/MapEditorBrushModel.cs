using System.Collections.Immutable;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public enum MapEditorRasterSourceStatus
{
    OBSOLETE,
    BRUSH_SOURCE,
}

public readonly record struct MapEditorBrushId(long Value);

public readonly record struct MapEditorStampId(long Value);

public readonly record struct MapEditorPoint(int X, int Y);

public readonly record struct MapEditorMapOrigin(long X, long Y)
{
    public static implicit operator MapEditorMapOrigin(MapEditorPoint point) =>
        new(point.X, point.Y);
}

public enum MapEditorProjectionMode
{
    REPEAT,
    STRETCH,
}

public sealed record MapEditorTextureReference(string Source, string Path)
{
    public const string PROJECT_SOURCE = "project";
    public const string LIBRARY_SOURCE_PREFIX = "library:";

    public string Location => Source == PROJECT_SOURCE
        ? $"res://{Path}"
        : Source.StartsWith(LIBRARY_SOURCE_PREFIX, StringComparison.Ordinal)
            ? $"library://{Source[LIBRARY_SOURCE_PREFIX.Length..]}/{Path}"
            : $"{Source}:{Path}";

    public static MapEditorTextureReference Project(string path) =>
        new(PROJECT_SOURCE, path.StartsWith("res://", StringComparison.Ordinal) ? path[6..] : path);

    public static MapEditorTextureReference Library(string libraryId, string path) =>
        new(LIBRARY_SOURCE_PREFIX + libraryId, path);
}

public readonly record struct MapEditorColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public string Html => $"#{Red:X2}{Green:X2}{Blue:X2}{Alpha:X2}";
}

public abstract record MapEditorBrushMaterial;

public sealed record MapEditorTextureMaterial(MapEditorTextureReference Reference) :
    MapEditorBrushMaterial;

public sealed record MapEditorSolidColorMaterial(MapEditorColor Color) : MapEditorBrushMaterial;

public sealed record MapEditorTextureProjection(
    MapEditorProjectionMode Mode,
    MapEditorPoint Origin,
    float ScaleX,
    float ScaleY,
    float Rotation);

public abstract record MapEditorBrushShape;

public sealed record MapEditorRectBrushShape(
    int X,
    int Y,
    int Width,
    int Height,
    float Rotation) : MapEditorBrushShape;

public sealed record MapEditorEllipseBrushShape(
    int X,
    int Y,
    int RadiusX,
    int RadiusY,
    float Rotation) : MapEditorBrushShape;

public sealed record MapEditorPolygonBrushShape(
    ImmutableArray<MapEditorPoint> Vertices) : MapEditorBrushShape;

public sealed record MapEditorBrush(
    MapEditorBrushId Id,
    string Name,
    MapEditorLayer Layer,
    MapEditorBrushShape Shape,
    MapEditorBrushMaterial Material,
    MapEditorTextureProjection Projection,
    bool Visible);

public sealed record MapEditorBrushDraft(
    string Name,
    MapEditorLayer Layer,
    MapEditorBrushShape Shape,
    MapEditorBrushMaterial Material,
    MapEditorTextureProjection Projection,
    bool Visible = true);

public sealed record MapEditorStamp(
    MapEditorStampId Id,
    string Name,
    MapEditorBrushDraft Brush);

public sealed record MapEditorLayerSource(
    ImmutableArray<MapEditorBrush> Brushes,
    // While dirty, this is the last saved artifact and may use the previous map bounds.
    MapEditorLayerAsset Baked,
    bool BakeDirty);

public sealed record MapEditorLayerSources(
    MapEditorLayerSource Background,
    MapEditorLayerSource Solid,
    MapEditorLayerSource Destructible)
{
    public MapEditorLayerSource Get(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => Background,
        MapEditorLayer.SOLID => Solid,
        MapEditorLayer.DESTRUCTIBLE => Destructible,
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    public MapEditorLayerSources Set(MapEditorLayer layer, MapEditorLayerSource source) =>
        layer switch
        {
            MapEditorLayer.BACKGROUND => this with { Background = source },
            MapEditorLayer.SOLID => this with { Solid = source },
            MapEditorLayer.DESTRUCTIBLE => this with { Destructible = source },
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
}

public sealed record MapEditorBrushDocument(
    int Version,
    long NextBrushId,
    MapEditorLayerSources Layers,
    MapEditorMapOrigin Origin = default,
    long NextStampId = 1,
    ImmutableArray<MapEditorStamp> Stamps = default)
{
    public const int CURRENT_VERSION = 2;

    public static MapEditorBrushDocument Empty(MapEditorLayers layers, bool bakeDirty = false)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return new MapEditorBrushDocument(CURRENT_VERSION, 1,
            new MapEditorLayerSources(
                new MapEditorLayerSource([], layers.Background, bakeDirty),
                new MapEditorLayerSource([], layers.Solid, bakeDirty),
                new MapEditorLayerSource([], layers.Destructible, bakeDirty)),
            NextStampId: 1, Stamps: []);
    }
}

public static class MapEditorBrushValidator
{
    public static ImmutableArray<ContentDiagnostic> Validate(
        MapEditorBrushDocument document,
        string source,
        int width,
        int height)
    {
        ImmutableArray<ContentDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<ContentDiagnostic>();
        if (document.Version != MapEditorBrushDocument.CURRENT_VERSION)
            Error($"This map uses editor data version {document.Version}, which isn't supported.");
        if (document.NextBrushId <= 0)
            Error("Brush numbering is invalid.");
        if (document.NextStampId <= 0)
            Error("Stamp numbering is invalid.");

        HashSet<long> ids = [];
        long maximumId = 0;
        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            MapEditorLayerSource layerSource = document.Layers.Get(layer);
            if (!layerSource.BakeDirty &&
                (layerSource.Baked.Width != width || layerSource.Baked.Height != height))
                Error($"The {layer.ToString().ToLowerInvariant()} layer size doesn't match the map.");
            foreach (MapEditorBrush brush in layerSource.Brushes)
            {
                if (brush.Id.Value <= 0)
                    Error("A brush has an invalid ID.");
                else if (!ids.Add(brush.Id.Value))
                    Error($"Brush ID {brush.Id.Value} is duplicated.");
                maximumId = Math.Max(maximumId, brush.Id.Value);
                if (brush.Layer != layer)
                    Error($"Brush {brush.Id.Value} belongs to {brush.Layer} but is stored in {layer}.");
                ValidateBrush(brush, Error);
            }
        }

        if (document.NextBrushId <= maximumId)
            Error("Brush numbering is invalid.");

        HashSet<long> stampIds = [];
        long maximumStampId = 0;
        foreach (MapEditorStamp stamp in document.Stamps.IsDefault ? [] : document.Stamps)
        {
            if (stamp.Id.Value <= 0)
                Error("A stamp has an invalid ID.");
            else if (!stampIds.Add(stamp.Id.Value))
                Error($"Stamp ID {stamp.Id.Value} is duplicated.");
            maximumStampId = Math.Max(maximumStampId, stamp.Id.Value);
            if (string.IsNullOrWhiteSpace(stamp.Name))
                Error($"Stamp {stamp.Id.Value} needs a name.");
            ValidateBrush(new MapEditorBrush(new MapEditorBrushId(stamp.Id.Value), stamp.Name,
                stamp.Brush.Layer, stamp.Brush.Shape, stamp.Brush.Material,
                stamp.Brush.Projection, stamp.Brush.Visible), Error);
        }
        if (document.NextStampId <= maximumStampId)
            Error("Stamp numbering is invalid.");

        return diagnostics.ToImmutable();

        void Error(string message) => diagnostics.Add(new ContentDiagnostic(
            ContentDiagnosticSeverity.ERROR, source, message));
    }

    private static void ValidateBrush(MapEditorBrush brush, Action<string> error)
    {
        switch (brush.Material)
        {
            case MapEditorTextureMaterial:
            case MapEditorSolidColorMaterial:
                break;
            case null:
                error($"Brush {brush.Id.Value} material is required.");
                break;
            default:
                error($"Brush {brush.Id.Value} material type is not supported.");
                break;
        }
        if (!float.IsFinite(brush.Projection.ScaleX) ||
            !float.IsFinite(brush.Projection.ScaleY) || brush.Projection.ScaleX <= 0 ||
            brush.Projection.ScaleY <= 0 || !float.IsFinite(brush.Projection.Rotation))
        {
            error($"Brush {brush.Id.Value} texture scale must be above zero, and its texture rotation must be a number.");
        }

        switch (brush.Shape)
        {
            case MapEditorRectBrushShape rect when rect.Width <= 0 || rect.Height <= 0 ||
                                                   !float.IsFinite(rect.Rotation):
                error($"Brush {brush.Id.Value} width and height must be above zero, and rotation must be a number.");
                break;
            case MapEditorEllipseBrushShape ellipse when ellipse.RadiusX <= 0 ||
                                                         ellipse.RadiusY <= 0 ||
                                                         !float.IsFinite(ellipse.Rotation):
                error($"Brush {brush.Id.Value} radii must be above zero, and rotation must be a number.");
                break;
            case MapEditorPolygonBrushShape polygon:
                if (!TryValidatePolygon(polygon.Vertices, out string? polygonError))
                    error($"Brush {brush.Id.Value} {polygonError}");
                break;
            case null:
                error($"Brush {brush.Id.Value} shape is required.");
                break;
        }
    }

    public static bool TryValidatePolygon(ImmutableArray<MapEditorPoint> vertices,
        out string? error)
    {
        if (vertices.IsDefault || vertices.Distinct().Count() < 3)
        {
            error = "polygon must contain at least three distinct vertices.";
            return false;
        }

        if (vertices.Distinct().Count() != vertices.Length)
        {
            error = "polygon vertices must be distinct.";
            return false;
        }

        for (int first = 0; first < vertices.Length; first++)
        {
            int firstNext = (first + 1) % vertices.Length;
            for (int second = first + 1; second < vertices.Length; second++)
            {
                int secondNext = (second + 1) % vertices.Length;
                if (first == second || firstNext == second || secondNext == first)
                    continue;
                if (SegmentsIntersect(vertices[first], vertices[firstNext],
                        vertices[second], vertices[secondNext]))
                {
                    error = "polygon edges must not self-intersect.";
                    return false;
                }
            }
        }

        Int128 twiceArea = 0;
        for (int index = 0; index < vertices.Length; index++)
        {
            MapEditorPoint current = vertices[index];
            MapEditorPoint next = vertices[(index + 1) % vertices.Length];
            twiceArea += (Int128)current.X * next.Y - (Int128)current.Y * next.X;
        }

        if (twiceArea == 0)
        {
            error = "polygon must enclose a non-zero area.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool SegmentsIntersect(MapEditorPoint a, MapEditorPoint b,
        MapEditorPoint c, MapEditorPoint d)
    {
        Int128 abC = Cross(a, b, c);
        Int128 abD = Cross(a, b, d);
        Int128 cdA = Cross(c, d, a);
        Int128 cdB = Cross(c, d, b);
        return (abC == 0 && OnSegment(a, b, c)) ||
               (abD == 0 && OnSegment(a, b, d)) ||
               (cdA == 0 && OnSegment(c, d, a)) ||
               (cdB == 0 && OnSegment(c, d, b)) ||
               ((abC < 0) != (abD < 0) && (cdA < 0) != (cdB < 0));
    }

    private static Int128 Cross(MapEditorPoint a, MapEditorPoint b, MapEditorPoint c) =>
        ((Int128)b.X - a.X) * ((Int128)c.Y - a.Y) -
        ((Int128)b.Y - a.Y) * ((Int128)c.X - a.X);

    private static bool OnSegment(MapEditorPoint a, MapEditorPoint b, MapEditorPoint point) =>
        point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X) &&
        point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);
}
