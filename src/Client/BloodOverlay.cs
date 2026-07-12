using Godot;

namespace Mortz.Client;

/// <summary>
/// Blood stains painted by landing gib particles. A separate transparent
/// overlay above the terrain layers, so staining never fights the carve
/// bookkeeping; GameMap erases it where carves remove ground.
/// </summary>
public partial class BloodOverlay : Sprite2D
{
    private static readonly Color _clear = new(0, 0, 0, 0);

    private Image _image = null!;
    private ImageTexture _texture = null!;
    private bool _dirty;
    private int _width, _height;

    public void Initialize(int width, int height)
    {
        _width = width;
        _height = height;
        _image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;
    }

    public void Paint(int x, int y, Color color)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;
        _image.SetPixel(x, y, color * 0.85f); // dried a shade darker than in flight
        _dirty = true;
    }

    /// <summary>The ground took the stain with it.</summary>
    public void Erase(int x, int y)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return;
        _image.SetPixel(x, y, _clear);
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        // Batch into one texture upload per frame however much landed.
        if (!_dirty)
            return;
        _dirty = false;
        _texture.Update(_image);
    }
}
