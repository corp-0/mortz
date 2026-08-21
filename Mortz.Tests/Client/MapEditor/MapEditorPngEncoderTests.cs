using Mortz.Client.MapEditor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

public sealed class MapEditorPngEncoderTests
{
    [Fact]
    public void EncodesRgbaRowsAsPng()
    {
        const int WIDTH = 32;
        const int HEIGHT = 24;
        byte[] rgba = new byte[WIDTH * HEIGHT * 4];
        new Random(17).NextBytes(rgba);
        using MemoryStream output = new();

        MapEditorPngEncoder.EncodeRgba(output,
            WIDTH, HEIGHT, (y, row) => rgba.AsSpan(y * row.Length, row.Length).CopyTo(row));

        output.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(output);
        byte[] decodedRgba = new byte[rgba.Length];
        decoded.CopyPixelDataTo(decodedRgba);
        Assert.Equal(WIDTH, decoded.Width);
        Assert.Equal(HEIGHT, decoded.Height);
        Assert.Equal(rgba, decodedRgba);
    }
}
