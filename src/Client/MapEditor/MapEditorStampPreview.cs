using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorStampPreview : Control
{
    private const float PADDING = 10f;
    private static readonly Color _background = new(0.055f, 0.065f, 0.08f);
    private static readonly Color _outline = new(0.82f, 0.87f, 0.95f, 0.9f);

    private MapEditorBrushDraft? _brush;
    private MapEditorCanvasResources? _resources;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _ExitTree()
    {
        Resized -= QueueRedraw;
    }

    public void Apply(MapEditorBrushDraft? brush, MapEditorCanvasResources resources)
    {
        _brush = brush;
        _resources = resources;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _background);
        if (_brush == null || _resources == null)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(0, Size.Y / 2f + 11f), "+",
                HorizontalAlignment.Center, Size.X, 30,
                new Color(0.65f, 0.7f, 0.78f, 0.8f));
            return;
        }

        Vector2[] mapPoints = MapEditorCanvasProjection.BrushOutline(_brush.Shape);
        if (mapPoints.Length < 3)
            return;
        MapEditorBounds bounds = MapEditorGeometry.Bounds(_brush.Shape);
        float width = MathF.Max(1f, bounds.Right - bounds.Left);
        float height = MathF.Max(1f, bounds.Bottom - bounds.Top);
        Vector2 available = new(MathF.Max(1f, Size.X - PADDING * 2f),
            MathF.Max(1f, Size.Y - PADDING * 2f));
        float scale = MathF.Min(available.X / width, available.Y / height);
        Vector2 mapCenter = new((bounds.Left + bounds.Right) / 2f,
            (bounds.Top + bounds.Bottom) / 2f);
        Vector2[] points = mapPoints.Select(point =>
            Size / 2f + (point - mapCenter) * scale).ToArray();

        (MapEditorTextureData data, ImageTexture texture, _) =
            _resources.Preview(_brush.Material);
        MapEditorBrush preview = new(new MapEditorBrushId(0), _brush.Name, _brush.Layer,
            _brush.Shape, _brush.Material, _brush.Projection, _brush.Visible);
        Vector2[] uvs = mapPoints.Select(point =>
            MapEditorCanvasProjection.PreviewUv(preview, data, point)).ToArray();
        Color[] colors = [Colors.White];
        DrawPolygon(points, colors, uvs, texture);
        DrawPolyline([.. points, points[0]], _outline, 2f, true);
    }
}
