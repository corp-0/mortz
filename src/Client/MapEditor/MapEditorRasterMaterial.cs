using System.Buffers.Binary;
using Godot;

namespace Mortz.Client.MapEditor;

public record MapEditorRasterMaterial : MapEditorBrushMaterial
{
    public MapEditorLayerAsset Image { get; }
    public MapEditorTextureData Texture { get; }

    public MapEditorRasterMaterial(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("Raster object data must be a PNG image.", nameof(png));
        uint width = BinaryPrimitives.ReadUInt32BigEndian(png[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png[20..24]);
        if (width is 0 or > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION ||
            height is 0 or > MapEditorMapBoundsFitter.MAX_TEXTURE_DIMENSION)
            throw new ArgumentException("Raster object dimensions must be between 1 and 8192 pixels.", nameof(png));
        using Image image = new();
        if (image.LoadPngFromBuffer(png.ToArray()) != Error.Ok ||
            image.GetWidth() != width || image.GetHeight() != height)
            throw new ArgumentException("Raster object PNG could not be decoded.", nameof(png));
        image.Convert(Godot.Image.Format.Rgba8);
        Image = new MapEditorLayerAsset(png, (int)width, (int)height);
        Texture = new MapEditorTextureData((int)width, (int)height, image.GetData());
    }
}
