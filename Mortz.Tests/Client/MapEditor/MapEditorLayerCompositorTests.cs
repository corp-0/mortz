using System.Collections.Immutable;
using Mortz.Client.MapEditor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorLayerCompositorTests
{
    [Fact]
    public void EmptyLayerIsTransparent()
    {
        MapEditorLayerCompositor compositor = Compositor([]);
        MapEditorLayerSource layer = Layer();

        MapEditorLayerCompositionResult result = compositor.Compose(layer, 3, 2);

        Assert.True(result.Succeeded);
        Decoded decoded = Decode(result.Baked!);
        Assert.Equal(3, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.All(Pixels(decoded), pixel => Assert.Equal(new Rgba(0, 0, 0, 0), pixel));
    }

    [Fact]
    public void SolidColorMaterialGeneratesUniformPixelsWithoutTextureResolution()
    {
        MapEditorBrush brush = new(new MapEditorBrushId(1), "color",
            MapEditorLayer.BACKGROUND, new MapEditorRectBrushShape(0, 0, 3, 2, 0),
            new MapEditorSolidColorMaterial(new MapEditorColor(0x20, 0x60, 0xa0, 0xc0)),
            new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                new MapEditorPoint(0, 0), 1, 1, 0), true);

        MapEditorLayerCompositionResult result = Compositor([]).Compose(Layer(brush), 3, 2);

        Assert.True(result.Succeeded, result.Error);
        Assert.All(Pixels(Decode(result.Baked!)),
            pixel => Assert.Equal(new Rgba(0x20, 0x60, 0xa0, 0xc0), pixel));
    }

    [Fact]
    public void AuthoringBoundsTranslateNegativeCoordinatesIntoTheTexture()
    {
        MapEditorLayerCompositor compositor = Compositor(
            [("res://pixel.png", Texture(1, 1, _red))]);
        MapEditorLayerSource layer = Layer(
            Rect("res://pixel.png", -20, 30, 2, 1, MapEditorProjectionMode.REPEAT));

        Decoded decoded = Decode(compositor.Compose(layer,
            new MapEditorMapBounds(-20, 30, 2, 1)).Baked!);

        Assert.Equal([_red, _red], Pixels(decoded));
    }

    [Fact]
    public void AuthoringBoundsLeaveUnpaintedExtentTransparent()
    {
        MapEditorLayerCompositor compositor = Compositor(
            [("res://pixel.png", Texture(1, 1, _red))]);
        MapEditorLayerSource layer = Layer(
            Rect("res://pixel.png", -2, 4, 1, 1, MapEditorProjectionMode.REPEAT));

        Decoded decoded = Decode(compositor.Compose(layer,
            new MapEditorMapBounds(-2, 4, 3, 1)).Baked!);

        Assert.Equal([_red, new Rgba(0, 0, 0, 0), new Rgba(0, 0, 0, 0)], Pixels(decoded));
    }

    [Fact]
    public void RepeatPreservesTexelDensityWhenRectangleIsResized()
    {
        MapEditorTextureData stripes = Texture(2, 1,
            new Rgba(255, 0, 0, 255), new Rgba(0, 255, 0, 255));
        MapEditorLayerCompositor compositor = Compositor([("res://stripes.png", stripes)]);

        Decoded four = Decode(compositor.Compose(Layer(
            Rect("res://stripes.png", 0, 0, 4, 1, MapEditorProjectionMode.REPEAT)), 4, 1).Baked!);
        Decoded six = Decode(compositor.Compose(Layer(
            Rect("res://stripes.png", 0, 0, 6, 1, MapEditorProjectionMode.REPEAT)), 6, 1).Baked!);

        Assert.Equal([_red, _green, _red, _green], Pixels(four));
        Assert.Equal([_red, _green, _red, _green, _red, _green], Pixels(six));
    }

    [Fact]
    public void StretchCoversBoundsExactlyOnce()
    {
        MapEditorTextureData stripes = Texture(2, 1, _red, _green);
        MapEditorLayerCompositor compositor = Compositor([("res://stripes.png", stripes)]);

        Decoded decoded = Decode(compositor.Compose(Layer(
            Rect("res://stripes.png", 0, 0, 4, 1, MapEditorProjectionMode.STRETCH)), 4, 1).Baked!);

        Assert.Equal([_red, _red, _green, _green], Pixels(decoded));
    }

    [Fact]
    public void RepeatWrapsNegativeCoordinatesAndAppliesProjectionRotation()
    {
        MapEditorTextureData texture = Texture(2, 2, _red, _green, _blue, _white);
        MapEditorLayerCompositor compositor = Compositor([("res://texture.png", texture)]);
        MapEditorBrush brush = Rect("res://texture.png", 0, 0, 2, 2,
                MapEditorProjectionMode.REPEAT) with
        {
            Projection = new MapEditorTextureProjection(MapEditorProjectionMode.REPEAT,
                    new MapEditorPoint(1, 1), 1, 1, 90),
        };

        Decoded decoded = Decode(compositor.Compose(Layer(brush), 2, 2).Baked!);

        Assert.Equal([_green, _white, _red, _blue], Pixels(decoded));
    }

    [Fact]
    public void StoredOrderUsesSourceOverAndClipsAtMapBoundary()
    {
        MapEditorTextureData red = Texture(1, 1, _red);
        MapEditorTextureData blueHalf = Texture(1, 1, new Rgba(0, 0, 255, 128));
        MapEditorLayerCompositor compositor = Compositor([
            ("res://red.png", red), ("res://blue.png", blueHalf),
        ]);
        MapEditorBrush bottom = Rect("res://red.png", -1, 0, 3, 1,
            MapEditorProjectionMode.REPEAT);
        MapEditorBrush top = Rect("res://blue.png", 0, 0, 1, 1,
            MapEditorProjectionMode.REPEAT, id: 2);

        Decoded decoded = Decode(compositor.Compose(Layer(bottom, top), 2, 1).Baked!);

        Assert.Equal(new Rgba(127, 0, 128, 255), Pixel(decoded, 0, 0));
        Assert.Equal(_red, Pixel(decoded, 1, 0));
    }

    [Fact]
    public void ConcavePolygonFillsActualShapeNotOnlyBounds()
    {
        MapEditorLayerCompositor compositor = Compositor([
            ("res://white.png",
                Texture(1, 1, _white))
        ]);
        MapEditorBrush polygon = Rect("res://white.png", 0, 0, 1, 1,
                MapEditorProjectionMode.STRETCH) with
        {
            Shape = new MapEditorPolygonBrushShape([
                    new MapEditorPoint(0, 0), new MapEditorPoint(3, 0),
                    new MapEditorPoint(3, 1), new MapEditorPoint(1, 1),
                    new MapEditorPoint(1, 3), new MapEditorPoint(0, 3),
                ]),
        };

        Decoded decoded = Decode(compositor.Compose(Layer(polygon), 3, 3).Baked!);

        Assert.Equal(_white, Pixel(decoded, 0, 2));
        Assert.Equal(new Rgba(0, 0, 0, 0), Pixel(decoded, 2, 2));
    }

    [Fact]
    public void RotatedPhysicalBrushHasOnlyHardCoverageAlpha()
    {
        MapEditorLayerCompositor compositor = Compositor([
            ("res://white.png",
                Texture(1, 1, _white))
        ]);
        MapEditorBrush brush = Rect("res://white.png", 1, 1, 3, 2,
                MapEditorProjectionMode.REPEAT) with
        {
            Layer = MapEditorLayer.SOLID,
            Shape = new MapEditorRectBrushShape(1, 1, 3, 2, 31),
        };

        Decoded decoded = Decode(compositor.Compose(Layer(brush), 6, 5).Baked!);

        Assert.Contains(Pixels(decoded), pixel => pixel.A == 255);
        Assert.All(Pixels(decoded), pixel => Assert.Contains(pixel.A, new byte[] { 0, 255 }));
    }

    [Fact]
    public void UnresolvedVisibleBrushFailsWithoutPlaceholderPixels()
    {
        MapEditorLayerCompositor compositor = Compositor([]);
        MapEditorBrush brush = Rect("res://missing.png", 0, 0, 2, 2,
            MapEditorProjectionMode.REPEAT);

        MapEditorLayerCompositionResult result = compositor.Compose(Layer(brush), 2, 2);

        Assert.False(result.Succeeded);
        Assert.Null(result.Baked);
        Assert.Equal(MapEditorTextureResolutionStatus.MISSING,
            Assert.Single(result.Unresolved).Status);
    }

    [Fact]
    public void MovingWholeBrushMovesProjectionOriginBySameDelta()
    {
        MapEditorBrush brush = Rect("res://texture.png", 2, 3, 4, 5,
            MapEditorProjectionMode.REPEAT);

        MapEditorBrush moved = MapEditorGeometry.Move(brush, -7, 9);

        Assert.Equal(new MapEditorRectBrushShape(-5, 12, 4, 5, 0), moved.Shape);
        Assert.Equal(new MapEditorPoint(-5, 12), moved.Projection.Origin);
    }

    [Fact]
    public void RotatedEllipseUsesAnalyticCoverageAndStretchBounds()
    {
        MapEditorLayerCompositor compositor = Compositor([
            ("res://white.png",
                Texture(1, 1, _white))
        ]);
        MapEditorBrush ellipse = Rect("res://white.png", 0, 0, 1, 1,
                MapEditorProjectionMode.STRETCH) with
        {
            Shape = new MapEditorEllipseBrushShape(3, 3, 2, 1, 90),
        };

        Decoded decoded = Decode(compositor.Compose(Layer(ellipse), 7, 7).Baked!);

        Assert.Equal(_white, Pixel(decoded, 3, 1));
        Assert.Equal(new Rgba(0, 0, 0, 0), Pixel(decoded, 1, 3));
        Assert.All(Pixels(decoded), pixel => Assert.Contains(pixel.A, new byte[] { 0, 255 }));
    }

    [Fact]
    public void ConcavePolygonStretchUsesItsAxisAlignedBoundsOnce()
    {
        MapEditorLayerCompositor compositor = Compositor([
            ("res://stripes.png",
                Texture(2, 1, _red, _green))
        ]);
        MapEditorBrush polygon = Rect("res://stripes.png", 0, 0, 1, 1,
                MapEditorProjectionMode.STRETCH) with
        {
            Shape = new MapEditorPolygonBrushShape([
                    new MapEditorPoint(0, 0),
                    new MapEditorPoint(4, 0), new MapEditorPoint(4, 1),
                    new MapEditorPoint(1, 1), new MapEditorPoint(1, 2),
                    new MapEditorPoint(0, 2)
                ]),
        };

        Decoded decoded = Decode(compositor.Compose(Layer(polygon), 4, 2).Baked!);

        Assert.Equal(_red, Pixel(decoded, 0, 0));
        Assert.Equal(_green, Pixel(decoded, 3, 0));
        Assert.Equal(new Rgba(0, 0, 0, 0), Pixel(decoded, 3, 1));
    }

    private static MapEditorLayerCompositor Compositor(
        IEnumerable<(string Path, MapEditorTextureData Texture)> textures)
    {
        Dictionary<string, MapEditorTextureData> values = textures.ToDictionary(
            item => item.Path, item => item.Texture);
        return new MapEditorLayerCompositor(new FakeResolver(values));
    }

    private static MapEditorLayerSource Layer(params MapEditorBrush[] brushes) => new(
        brushes.ToImmutableArray(),
        new MapEditorLayerAsset([1], 1, 1), true);

    private static MapEditorBrush Rect(string path, int x, int y, int width, int height,
        MapEditorProjectionMode mode, long id = 1) => new(new MapEditorBrushId(id), "brush",
        MapEditorLayer.BACKGROUND, new MapEditorRectBrushShape(x, y, width, height, 0),
        new MapEditorTextureMaterial(MapEditorTextureReference.Project(path)),
        new MapEditorTextureProjection(mode, new MapEditorPoint(x, y), 1, 1, 0), true);

    private static MapEditorTextureData Texture(int width, int height,
        params Rgba[] colors)
    {
        byte[] rgba = colors.SelectMany(color => new[]
            { color.R, color.G, color.B, color.A }).ToArray();
        return new MapEditorTextureData(width, height, rgba);
    }

    private static Decoded Decode(MapEditorLayerAsset asset)
    {
        using MemoryStream png = new(asset.Png.ToArray(), writable: false);
        using Image<Rgba32> image = Image.Load<Rgba32>(png);
        byte[] rgba = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rgba);
        return new Decoded(image.Width, image.Height, rgba);
    }

    private static Rgba Pixel(Decoded image, int x, int y)
    {
        int offset = (y * image.Width + x) * 4;
        return new Rgba(image.Data[offset], image.Data[offset + 1],
            image.Data[offset + 2], image.Data[offset + 3]);
    }

    private static Rgba[] Pixels(Decoded image) => Enumerable.Range(0,
        image.Width * image.Height).Select(index => new Rgba(image.Data[index * 4],
        image.Data[index * 4 + 1], image.Data[index * 4 + 2],
        image.Data[index * 4 + 3])).ToArray();

    private static readonly Rgba _red = new(255, 0, 0, 255);
    private static readonly Rgba _green = new(0, 255, 0, 255);
    private static readonly Rgba _blue = new(0, 0, 255, 255);
    private static readonly Rgba _white = new(255, 255, 255, 255);

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);

    private sealed record Decoded(int Width, int Height, byte[] Data);

    private sealed class FakeResolver(IReadOnlyDictionary<string, MapEditorTextureData> textures)
        : IMapEditorTextureResolver
    {
        public MapEditorTextureResolution Resolve(MapEditorTextureReference reference) =>
            textures.TryGetValue(reference.Location, out MapEditorTextureData? texture)
                ? new MapEditorTextureResolution(MapEditorTextureResolutionStatus.RESOLVED,
                    reference, texture, "resolved", reference.Location)
                : new MapEditorTextureResolution(MapEditorTextureResolutionStatus.MISSING,
                    reference, null, "missing");

        public void Invalidate()
        {
        }
    }
}
