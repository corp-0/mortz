using System.Collections.Immutable;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorUnresolvedBrush(
    MapEditorBrushId Id,
    string Name,
    MapEditorTextureReference Reference,
    MapEditorTextureResolutionStatus Status,
    string Message);

public sealed record MapEditorLayerCompositionResult(
    MapEditorLayerAsset? Baked,
    ImmutableArray<MapEditorUnresolvedBrush> Unresolved,
    string? Error,
    MapEditorLayerCompositionMetrics? Metrics = null)
{
    public bool Succeeded => Baked != null && Error == null && Unresolved.IsEmpty;
}

public readonly record struct MapEditorLayerCompositionMetrics(
    long ScanlineBufferBytes,
    long IdatBufferBytes,
    long EncodedPngBytes,
    long FullSizeRawBufferBytes,
    long InMemoryEncodedStagingBufferBytes,
    long FinalEncodedBufferCapacity);

public interface IMapEditorLayerCompositor
{
    MapEditorLayerCompositionResult Compose(MapEditorLayerSource layer, int width, int height);

    MapEditorLayerCompositionResult Compose(MapEditorLayerSource layer, MapEditorMapBounds bounds) =>
        bounds.Width is <= 0 or > int.MaxValue || bounds.Height is <= 0 or > int.MaxValue
            ? new MapEditorLayerCompositionResult(null, [],
                $"Layer size {bounds.Width} x {bounds.Height} can't be saved.")
            : Compose(layer, (int)bounds.Width, (int)bounds.Height);
}

public sealed class MapEditorLayerCompositor(IMapEditorTextureResolver textures) : IMapEditorLayerCompositor
{
    private readonly IMapEditorTextureResolver
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));

    public MapEditorLayerCompositionResult Compose(MapEditorLayerSource layer,
        MapEditorMapBounds bounds) => ComposeCore(layer, bounds);

    public MapEditorLayerCompositionResult Compose(MapEditorLayerSource layer,
        int width, int height) => ComposeCore(layer, new MapEditorMapBounds(0, 0, width, height));

    private MapEditorLayerCompositionResult ComposeCore(MapEditorLayerSource layer,
        MapEditorMapBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (bounds.Width is <= 0 or > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION ||
            bounds.Height is <= 0 or > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION)
        {
            return new MapEditorLayerCompositionResult(null, [],
                $"Layer size {bounds.Width} x {bounds.Height} is too large. The maximum is " +
                $"{MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION} x " +
                $"{MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION}.");
        }

        int width = (int)bounds.Width;
        int height = (int)bounds.Height;

        foreach (MapEditorBrush brush in layer.Brushes)
        {
            string? validationError = brush.Shape switch
            {
                MapEditorRectBrushShape rect when rect.Width <= 0 || rect.Height <= 0 =>
                    "width and height must be above zero.",
                MapEditorEllipseBrushShape ellipse when ellipse.RadiusX <= 0 ||
                                                        ellipse.RadiusY <= 0 =>
                    "radii must be above zero.",
                MapEditorPolygonBrushShape polygon when
                    !MapEditorBrushValidator.TryValidatePolygon(polygon.Vertices, out _) =>
                    PolygonError(polygon),
                _ => null,
            };
            if (validationError != null)
                return new MapEditorLayerCompositionResult(null, [],
                    $"Brush {brush.Id.Value} {validationError}");
        }

        ImmutableArray<MapEditorUnresolvedBrush>.Builder unresolved =
            ImmutableArray.CreateBuilder<MapEditorUnresolvedBrush>();
        List<RenderBrush> brushes = [];
        foreach (MapEditorBrush brush in layer.Brushes)
        {
            if (!brush.Visible)
                continue;
            MapEditorTextureData textureData;
            if (brush.Material is MapEditorSolidColorMaterial solid)
            {
                textureData = MapEditorTextureData.Solid(solid.Color);
            }
            else if (brush.Material is MapEditorTextureMaterial texture)
            {
                MapEditorTextureResolution resolution = _textures.Resolve(texture.Reference);
                if (!resolution.IsResolved)
                {
                    unresolved.Add(new MapEditorUnresolvedBrush(brush.Id, brush.Name,
                        texture.Reference, resolution.Status, resolution.Message));
                    continue;
                }
                textureData = resolution.Texture!;
            }
            else
            {
                return new MapEditorLayerCompositionResult(null, [],
                    $"Brush {brush.Id.Value} uses a material that isn't supported.");
            }

            brushes.Add(new RenderBrush(brush, textureData, GetBounds(brush.Shape)));
        }

        if (unresolved.Count > 0)
            return new MapEditorLayerCompositionResult(null, unresolved.ToImmutable(), null);

        try
        {
            string path = Path.Combine(Path.GetTempPath(),
                $"mortz-map-layer-{Guid.NewGuid():N}.png");
            byte[] ownedPng;
            MapEditorPngEncodingMetrics encoding;
            long encodedLength;
            using (FileStream png = new(path, FileMode.CreateNew, FileAccess.ReadWrite,
                       FileShare.None, 64 * 1024,
                       FileOptions.SequentialScan | FileOptions.DeleteOnClose))
            {
                encoding = MapEditorPngEncoder.EncodeRgba(png, width, height,
                    (y, row) => RenderRow(row, width, y, bounds.X, bounds.Y, brushes));
                png.Flush();
                encodedLength = png.Length;
                ownedPng = GC.AllocateUninitializedArray<byte>(checked((int)encodedLength));
                png.Position = 0;
                png.ReadExactly(ownedPng);
            }

            return new MapEditorLayerCompositionResult(
                MapEditorLayerAsset.AdoptOwnedPng(ownedPng, width, height), [], null,
                new MapEditorLayerCompositionMetrics(encoding.ScanlineBufferBytes,
                    encoding.IdatBufferBytes, encodedLength, FullSizeRawBufferBytes: 0,
                    InMemoryEncodedStagingBufferBytes: 0,
                    FinalEncodedBufferCapacity: ownedPng.LongLength));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or OverflowException or IOException or
                                              UnauthorizedAccessException)
        {
            return new MapEditorLayerCompositionResult(null, [], exception.Message);
        }
    }

    private static string PolygonError(MapEditorPolygonBrushShape polygon)
    {
        MapEditorBrushValidator.TryValidatePolygon(polygon.Vertices, out string? error);
        return error ?? "polygon isn't valid.";
    }

    private static void RenderRow(Span<byte> destination, int width, int y,
        long originX, long originY, List<RenderBrush> brushes)
    {
        destination.Clear();
        foreach (RenderBrush rendered in brushes)
        {
            RasterizeRow(destination, width, y, originX, originY, rendered);
        }
    }

    private static void RasterizeRow(Span<byte> destination, int width, int y,
        long originX, long originY, RenderBrush rendered)
    {
        int minimumY = (int)Math.Clamp(Math.Floor(rendered.Bounds.MinimumY - originY),
            int.MinValue, int.MaxValue);
        int maximumY = (int)Math.Clamp(Math.Ceiling(rendered.Bounds.MaximumY - originY),
            int.MinValue, int.MaxValue);
        if (y < minimumY || y >= maximumY)
            return;
        int minimumX = (int)Math.Clamp(Math.Floor(rendered.Bounds.MinimumX - originX), 0, width);
        int maximumX = (int)Math.Clamp(Math.Ceiling(rendered.Bounds.MaximumX - originX), 0, width);
        for (int x = minimumX; x < maximumX; x++)
        {
            double sampleX = originX + x + 0.5d;
            double sampleY = originY + y + 0.5d;
            if (!Contains(rendered.Brush.Shape, sampleX, sampleY))
                continue;
            (int textureX, int textureY) = Project(rendered.Brush, rendered.Texture,
                sampleX, sampleY);
            int sourceOffset = (textureY * rendered.Texture.Width + textureX) * 4;
            int destinationOffset = x * 4;
            BlendSourceOver(destination, destinationOffset,
                rendered.Texture.Pixels, sourceOffset);
        }
    }

    internal static (int X, int Y) Project(MapEditorBrush brush,
        MapEditorTextureData texture, double x, double y)
    {
        if (brush.Projection.Mode == MapEditorProjectionMode.REPEAT)
        {
            (double localX, double localY) = InverseRotate(x, y,
                brush.Projection.Origin.X, brush.Projection.Origin.Y,
                brush.Projection.Rotation);
            long projectedX = checked((long)Math.Floor(
                (localX - brush.Projection.Origin.X) / brush.Projection.ScaleX));
            long projectedY = checked((long)Math.Floor(
                (localY - brush.Projection.Origin.Y) / brush.Projection.ScaleY));
            return (PositiveModulo(projectedX, texture.Width),
                PositiveModulo(projectedY, texture.Height));
        }

        (double shapeX, double shapeY) = ToShapeLocal(brush.Shape, x, y);
        Bounds bounds = GetLocalBounds(brush.Shape);
        double u = (shapeX - bounds.MinimumX) / (bounds.MaximumX - bounds.MinimumX);
        double v = (shapeY - bounds.MinimumY) / (bounds.MaximumY - bounds.MinimumY);
        int textureX = Math.Clamp((int)Math.Floor(u * texture.Width), 0, texture.Width - 1);
        int textureY = Math.Clamp((int)Math.Floor(v * texture.Height), 0, texture.Height - 1);
        return (textureX, textureY);
    }

    internal static bool Contains(MapEditorBrushShape shape, double x, double y)
    {
        switch (shape)
        {
            case MapEditorRectBrushShape rect:
                {
                    (double localX, double localY) = ToShapeLocal(rect, x, y);
                    return localX >= rect.X && localX < rect.X + rect.Width &&
                           localY >= rect.Y && localY < rect.Y + rect.Height;
                }
            case MapEditorEllipseBrushShape ellipse:
                {
                    (double localX, double localY) = ToShapeLocal(ellipse, x, y);
                    double normalizedX = (localX - ellipse.X) / ellipse.RadiusX;
                    double normalizedY = (localY - ellipse.Y) / ellipse.RadiusY;
                    return normalizedX * normalizedX + normalizedY * normalizedY <= 1d;
                }
            case MapEditorPolygonBrushShape polygon:
                return PolygonContains(polygon.Vertices, x, y);
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    private static bool PolygonContains(ImmutableArray<MapEditorPoint> vertices,
        double x, double y)
    {
        bool inside = false;
        for (int current = 0, previous = vertices.Length - 1;
             current < vertices.Length;
             previous = current++)
        {
            MapEditorPoint a = vertices[previous];
            MapEditorPoint b = vertices[current];
            if (PointOnSegment(a, b, x, y))
                return true;
            bool crosses = (a.Y > y) != (b.Y > y) &&
                           x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    private static bool PointOnSegment(MapEditorPoint a, MapEditorPoint b, double x, double y)
    {
        double cross = ((double)b.X - a.X) * (y - a.Y) -
                       ((double)b.Y - a.Y) * (x - a.X);
        return Math.Abs(cross) <= 1e-6 && x >= Math.Min(a.X, b.X) &&
               x <= Math.Max(a.X, b.X) && y >= Math.Min(a.Y, b.Y) &&
               y <= Math.Max(a.Y, b.Y);
    }

    private static Bounds GetBounds(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape rect => RotatedBounds(rect.X, rect.Y,
            (long)rect.X + rect.Width, (long)rect.Y + rect.Height,
            rect.X + rect.Width / 2d, rect.Y + rect.Height / 2d, rect.Rotation),
        MapEditorEllipseBrushShape ellipse => EllipseBounds(ellipse),
        MapEditorPolygonBrushShape polygon => PolygonBounds(polygon.Vertices),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static Bounds GetLocalBounds(MapEditorBrushShape shape) => shape switch
    {
        MapEditorRectBrushShape rect => new(rect.X, rect.Y,
            (long)rect.X + rect.Width, (long)rect.Y + rect.Height),
        MapEditorEllipseBrushShape ellipse => new(ellipse.X - ellipse.RadiusX,
            ellipse.Y - ellipse.RadiusY, ellipse.X + ellipse.RadiusX,
            ellipse.Y + ellipse.RadiusY),
        MapEditorPolygonBrushShape polygon => PolygonBounds(polygon.Vertices),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static Bounds EllipseBounds(MapEditorEllipseBrushShape ellipse)
    {
        double radians = ellipse.Rotation * Math.PI / 180d;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double halfWidth = Math.Sqrt((double)ellipse.RadiusX * ellipse.RadiusX * cos * cos +
                                     (double)ellipse.RadiusY * ellipse.RadiusY * sin * sin);
        double halfHeight = Math.Sqrt((double)ellipse.RadiusX * ellipse.RadiusX * sin * sin +
                                      (double)ellipse.RadiusY * ellipse.RadiusY * cos * cos);
        return new Bounds(ellipse.X - halfWidth, ellipse.Y - halfHeight,
            ellipse.X + halfWidth, ellipse.Y + halfHeight);
    }

    private static Bounds PolygonBounds(ImmutableArray<MapEditorPoint> vertices) => new(
        vertices.Min(point => point.X), vertices.Min(point => point.Y),
        vertices.Max(point => point.X), vertices.Max(point => point.Y));

    private static Bounds RotatedBounds(double minimumX, double minimumY,
        double maximumX, double maximumY, double centerX, double centerY, float rotation)
    {
        (double X, double Y)[] corners =
        [
            Rotate(minimumX, minimumY, centerX, centerY, rotation),
            Rotate(maximumX, minimumY, centerX, centerY, rotation),
            Rotate(maximumX, maximumY, centerX, centerY, rotation),
            Rotate(minimumX, maximumY, centerX, centerY, rotation),
        ];
        return new Bounds(corners.Min(point => point.X), corners.Min(point => point.Y),
            corners.Max(point => point.X), corners.Max(point => point.Y));
    }

    private static (double X, double Y) ToShapeLocal(MapEditorBrushShape shape,
        double x, double y) => shape switch
        {
            MapEditorRectBrushShape rect => InverseRotate(x, y,
                rect.X + rect.Width / 2d, rect.Y + rect.Height / 2d, rect.Rotation),
            MapEditorEllipseBrushShape ellipse => InverseRotate(x, y,
                ellipse.X, ellipse.Y, ellipse.Rotation),
            MapEditorPolygonBrushShape => (x, y),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

    private static (double X, double Y) InverseRotate(double x, double y,
        double centerX, double centerY, float degrees) =>
        Rotate(x, y, centerX, centerY, -degrees);

    private static (double X, double Y) Rotate(double x, double y,
        double centerX, double centerY, float degrees)
    {
        double radians = degrees * Math.PI / 180d;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double deltaX = x - centerX;
        double deltaY = y - centerY;
        return (centerX + deltaX * cos - deltaY * sin,
            centerY + deltaX * sin + deltaY * cos);
    }

    private static int PositiveModulo(long value, int modulus)
    {
        long remainder = value % modulus;
        return (int)(remainder < 0 ? remainder + modulus : remainder);
    }

    internal static void BlendSourceOver(Span<byte> destination, int destinationOffset,
        ReadOnlySpan<byte> source, int sourceOffset)
    {
        int sourceAlpha = source[sourceOffset + 3];
        if (sourceAlpha == 0)
            return;
        int destinationAlpha = destination[destinationOffset + 3];
        int inverseSourceAlpha = 255 - sourceAlpha;
        int alphaNumerator = sourceAlpha * 255 + destinationAlpha * inverseSourceAlpha;
        if (alphaNumerator == 0)
            return;

        for (int channel = 0; channel < 3; channel++)
        {
            int colorNumerator = source[sourceOffset + channel] * sourceAlpha * 255 +
                                 destination[destinationOffset + channel] * destinationAlpha * inverseSourceAlpha;
            destination[destinationOffset + channel] = (byte)((colorNumerator +
                                                               alphaNumerator / 2) / alphaNumerator);
        }

        destination[destinationOffset + 3] = (byte)((alphaNumerator + 127) / 255);
    }

    private readonly record struct Bounds(
        double MinimumX,
        double MinimumY,
        double MaximumX,
        double MaximumY);

    private sealed record RenderBrush(
        MapEditorBrush Brush,
        MapEditorTextureData Texture,
        Bounds Bounds);
}
