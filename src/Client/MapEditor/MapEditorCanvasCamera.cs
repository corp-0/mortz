using Godot;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorCanvasCamera
{
    public MapEditorMapBounds Bounds { get; private set; }
    public Vector2 Position { get; private set; }
    public float Zoom { get; private set; } = 1f;

    public void ApplyBounds(MapEditorMapBounds bounds, bool reset)
    {
        Bounds = bounds;
        if (reset)
        {
            Position = BoundsCenter();
            Zoom = 1f;
        }
    }

    public bool Reset()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return false;
        Position = BoundsCenter();
        Zoom = 1f;
        return true;
    }

    public bool FrameMap(Vector2 viewportSize)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0 ||
            viewportSize.X <= 0 || viewportSize.Y <= 0)
            return false;
        Position = BoundsCenter();
        Zoom = MapEditorGeometry.ClampZoom(MathF.Min(
            viewportSize.X / Bounds.Width,
            viewportSize.Y / Bounds.Height));
        return true;
    }

    public bool Frame(MapEditorBounds bounds, Vector2 viewportSize)
    {
        if (viewportSize.X <= 0 || viewportSize.Y <= 0)
            return false;
        float width = Math.Max(32, bounds.Right - bounds.Left);
        float height = Math.Max(32, bounds.Bottom - bounds.Top);
        Position = new Vector2((bounds.Left + bounds.Right) / 2f,
            (bounds.Top + bounds.Bottom) / 2f);
        Zoom = MapEditorGeometry.ClampZoom(MathF.Min(
            viewportSize.X / (width * 1.25f), viewportSize.Y / (height * 1.25f)));
        return true;
    }

    public void Move(Vector2 localDelta) => Position += localDelta / Zoom;

    public void SetZoom(float zoom, Vector2 anchor, Vector2 viewportSize)
    {
        Vector2 mapAtAnchor = LocalToMap(anchor, viewportSize);
        Zoom = MapEditorGeometry.ClampZoom(zoom);
        Position = mapAtAnchor - (anchor - viewportSize / 2f) / Zoom;
    }

    public Vector2 LocalToMap(Vector2 point, Vector2 viewportSize) =>
        Position + (point - viewportSize / 2f) / Zoom;

    public Vector2 MapToLocal(Vector2 point, Vector2 viewportSize) =>
        viewportSize / 2f + (point - Position) * Zoom;

    public Rect2 MapRect(Vector2 viewportSize) => new(
        MapToLocal(new Vector2(Bounds.X, Bounds.Y), viewportSize),
        new Vector2(Bounds.Width, Bounds.Height) * Zoom);

    public Vector2 BoundsCenter() => new(
        (float)(Bounds.X + Bounds.Width / 2d),
        (float)(Bounds.Y + Bounds.Height / 2d));
}
