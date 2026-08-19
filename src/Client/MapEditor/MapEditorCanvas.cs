using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;
using SimVec2 = Mortz.Core.Sim.Vec2;

namespace Mortz.Client.MapEditor;

public enum MapEditorTool
{
    SELECT,
    RECT,
    CIRCLE,
    SPAWN,
}

public partial class MapEditorCanvas : Control
{
    private const float CAMERA_SPEED = 700f;
    private const float HANDLE_RADIUS = 7f;
    private const float ZOOM_STEP = 1.2f;

    private static readonly Color _zoneFill = new(0.15f, 0.65f, 1f, 0.16f);
    private static readonly Color _zoneLine = new(0.25f, 0.8f, 1f, 0.9f);
    private static readonly Color _selectedFill = new(1f, 0.65f, 0.1f, 0.2f);
    private static readonly Color _selectedLine = new(1f, 0.75f, 0.2f, 1f);

    private ImageTexture? _background;
    private ImageTexture? _solid;
    private ImageTexture? _destructible;
    [Export] private Texture2D _playerTexture = null!;
    private readonly MapEditorInteraction _interaction = new();
    private MapEditorSnapshot? _snapshot;
    private Vector2I _mapSize;
    private Vector2 _cameraPosition;
    private Vector2 _cursorMapPosition;
    private bool _panning;
    private bool _cursorVisible;

    public event Action<MapEditorZoneId?>? SelectionChanged;
    public event Action<MapEditorSpawnId?>? SpawnSelectionChanged;
    public event Action<MapEditorZoneDraft?>? ZonePreviewChanged;
    public event Action<MapSpawnPoint?>? SpawnPreviewChanged;
    public event Action<MapEditorZoneDraft>? ZoneAddRequested;
    public event Action<MapEditorZoneId, MapEditorZoneDraft>? ZoneReplaceRequested;
    public event Action<MapSpawnPoint>? SpawnAddRequested;
    public event Action<MapEditorSpawnId, MapSpawnPoint>? SpawnReplaceRequested;
    public event Action<int, int>? CursorMoved;
    public event Action<float>? ZoomChanged;

    private MapEditorTool _tool;

    public MapEditorTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            MouseDefaultCursorShape = value == MapEditorTool.SELECT
                ? CursorShape.Arrow : CursorShape.Cross;
            if (value != MapEditorTool.SELECT)
                Select(null);
            if (value != MapEditorTool.SPAWN)
                SelectSpawn(null);
            QueueRedraw();
        }
    }
    public MapEditorZoneId? SelectedZoneId => _interaction.SelectedZoneId;
    public MapEditorSpawnId? SelectedSpawnId => _interaction.SelectedSpawnId;
    public bool ShowBackground { get; set; } = true;
    public bool ShowSolid { get; set; } = true;
    public bool ShowDestructible { get; set; } = true;
    public bool ShowZones { get; set; } = true;
    public bool ShowSpawns { get; set; } = true;
    private float Zoom { get; set; } = 1f;

    public override void _Ready()
    {
        _interaction.ZoneSelectionChanged += OnZoneSelectionChanged;
        _interaction.SpawnSelectionChanged += OnSpawnSelectionChanged;
        _interaction.ZonePreviewChanged += OnZonePreviewChanged;
        _interaction.SpawnPreviewChanged += OnSpawnPreviewChanged;
        _interaction.ZoneAddRequested += draft => ZoneAddRequested?.Invoke(draft);
        _interaction.ZoneReplaceRequested += (id, draft) => ZoneReplaceRequested?.Invoke(id, draft);
        _interaction.SpawnAddRequested += spawn => SpawnAddRequested?.Invoke(spawn);
        _interaction.SpawnReplaceRequested += (id, spawn) => SpawnReplaceRequested?.Invoke(id, spawn);
        Tool = MapEditorTool.SELECT;
        ClipContents = true;
        Resized += OnResized;
    }

    public override void _ExitTree() => Resized -= OnResized;

    public void Apply(MapEditorUpdate update)
    {
        bool resetView = update.Change is MapEditorOpened or MapEditorReloaded;
        foreach (MapEditorLayer layer in MapEditorCanvasLayerPlan.LayersToDecode(update.Change))
        {
            ImageTexture texture = DecodeTexture(update.Snapshot.Layers, layer);
            switch (layer)
            {
                case MapEditorLayer.BACKGROUND:
                    _background = texture;
                    break;
                case MapEditorLayer.SOLID:
                    _solid = texture;
                    break;
                case MapEditorLayer.DESTRUCTIBLE:
                    _destructible = texture;
                    break;
            }
        }

        _snapshot = update.Snapshot;
        _mapSize = new Vector2I(update.Snapshot.Width, update.Snapshot.Height);
        _interaction.Apply(update);
        switch (update.Change)
        {
            case MapEditorZoneReplaced zoneReplaced when
                SelectedZoneId == zoneReplaced.Id:
                SelectionChanged?.Invoke(zoneReplaced.Id);
                break;
            case MapEditorSpawnReplaced spawnReplaced when
                SelectedSpawnId == spawnReplaced.Id:
                SpawnSelectionChanged?.Invoke(spawnReplaced.Id);
                break;
        }

        if (resetView)
        {
            _cameraPosition = new Vector2(_mapSize.X / 2f, _mapSize.Y / 2f);
            Zoom = 1f;
            ZoomChanged?.Invoke(Zoom);
        }
        QueueRedraw();
    }

    public void Select(MapEditorZoneId? id) => _interaction.SelectZone(id);

    public void SelectSpawn(MapEditorSpawnId? id) => _interaction.SelectSpawn(id);

    public void CancelInteraction()
    {
        _interaction.Cancel();
        QueueRedraw();
    }

    public void ZoomIn() => SetZoom(Zoom * ZOOM_STEP, Size / 2f);
    public void ZoomOut() => SetZoom(Zoom / ZOOM_STEP, Size / 2f);
    public void ResetView()
    {
        if (_mapSize.X == 0)
            return;
        ApplyView(MapEditorGeometry.ResetView(_mapSize.X, _mapSize.Y));
    }

    public void FrameMap()
    {
        if (_mapSize.X == 0 || Size.X <= 0 || Size.Y <= 0)
            return;
        ApplyView(MapEditorGeometry.FrameView(_mapSize.X, _mapSize.Y, Size.X, Size.Y));
    }

    private void ApplyView(MapEditorView view)
    {
        _cameraPosition = new Vector2(view.CameraPosition.X, view.CameraPosition.Y);
        Zoom = view.Zoom;
        ZoomChanged?.Invoke(Zoom);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 mapRect = MapRect();
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.035f, 0.04f, 0.05f));
        if (_background != null && ShowBackground)
            DrawTextureRect(_background, mapRect, false);
        if (_solid != null && ShowSolid)
            DrawTextureRect(_solid, mapRect, false);
        if (_destructible != null && ShowDestructible)
            DrawTextureRect(_destructible, mapRect, false);
        if (_snapshot == null)
            return;

        if (ShowZones)
        {
            foreach (MapEditorZone zone in _snapshot.Zones)
            {
                MapEditorZoneDraft displayed = zone.Id == SelectedZoneId &&
                    _interaction.ZonePreview is { } preview ? preview : Draft(zone);
                DrawZone(displayed, zone.Id == SelectedZoneId);
            }
            if (_interaction.ZonePreview is { } newZone && SelectedZoneId == null)
                DrawZone(newZone, true);
        }
        if (ShowSpawns)
            DrawSpawns();
        if (_interaction.SpawnPreview == null && Tool == MapEditorTool.SPAWN && _cursorVisible)
            DrawSpawnPreview();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_snapshot == null || _mapSize.X == 0)
            return;
        if (_interaction.Dragging && @event.IsActionPressed("ui_cancel"))
        {
            CancelInteraction();
            AcceptEvent();
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                {
                    float factor = wheel.ButtonIndex == MouseButton.WheelUp ? ZOOM_STEP : 1f / ZOOM_STEP;
                    SetZoom(Zoom * factor, wheel.Position);
                    AcceptEvent();
                    return;
                }
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed;
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                {
                    if (button.Pressed)
                    {
                        if (!MapRect().HasPoint(button.Position))
                            return;
                        BeginDrag(LocalToMap(button.Position));
                    }
                    else if (_interaction.Dragging)
                        EndDrag(LocalToMap(button.Position));
                    AcceptEvent();
                    return;
                }
            case InputEventMouseMotion motion when _panning:
                ReportCursor(motion.Position);
                MoveCamera(-motion.Relative);
                AcceptEvent();
                return;
            case InputEventMouseMotion dragMotion when _interaction.Dragging:
                ReportCursor(dragMotion.Position);
                UpdateDrag(LocalToMap(dragMotion.Position));
                AcceptEvent();
                return;
            case InputEventMouseMotion hoverMotion:
                ReportCursor(hoverMotion.Position);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit)
            return;
        Vector2 direction = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A)) direction.X--;
        if (Input.IsPhysicalKeyPressed(Key.D)) direction.X++;
        if (Input.IsPhysicalKeyPressed(Key.W)) direction.Y--;
        if (Input.IsPhysicalKeyPressed(Key.S)) direction.Y++;
        if (direction == Vector2.Zero)
            return;
        MoveCamera(direction.Normalized() * CAMERA_SPEED * (float)delta);
    }

    private void BeginDrag(Vector2 point)
    {
        point = ClampToMap(point);
        switch (Tool)
        {
            case MapEditorTool.SPAWN:
                {
                    MapEditorSpawn? hit = HitTestSpawn(point);
                    if (hit == null)
                    {
                        MapSpawnPoint spawn = new((int)point.X, (int)point.Y);
                        _interaction.BeginSpawnCreation(spawn, Sim(point));
                    }
                    else
                        _interaction.BeginSpawnMove(hit.Value.Id, Sim(point));
                    return;
                }
            case MapEditorTool.SELECT:
                {
                    if (ShowSpawns)
                    {
                        MapEditorSpawn? spawn = HitTestSpawn(point);
                        if (spawn != null)
                        {
                            _interaction.BeginSpawnMove(spawn.Value.Id, Sim(point));
                            return;
                        }
                    }
                    SelectSpawn(null);
                    MapEditorZone? selected = SelectedZone();
                    if (selected != null && TryStartHandleDrag(selected, point))
                        return;
                    MapEditorZone? hit = HitTestZone(point);
                    Select(hit?.Id);
                    if (hit == null)
                        return;
                    TryStartHandleDrag(hit, point);
                    return;
                }
        }

        string name = UniqueName();
        MapZoneShape shape = Tool == MapEditorTool.RECT
            ? new RectMapZoneShape((int)point.X, (int)point.Y, 1, 1)
            : new EllipseMapZoneShape((int)point.X, (int)point.Y, 1, 1);
        MapEditorZoneDraft draft = new(name, [], shape, []);
        _interaction.BeginZoneCreation(
            Tool == MapEditorTool.RECT ? MapEditorZoneDrag.CREATE_RECT :
                MapEditorZoneDrag.CREATE_CIRCLE,
            draft, Sim(point));
    }

    private bool TryStartHandleDrag(MapEditorZone zone, Vector2 point)
    {
        MapZoneHandle handle = MapEditorGeometry.PickHandle(zone.Shape,
            Sim(point), HANDLE_RADIUS, out SimVec2 anchor);
        if (handle == MapZoneHandle.NONE)
            return false;
        return _interaction.BeginZoneDrag(zone.Id,
            handle == MapZoneHandle.MOVE ? MapEditorZoneDrag.MOVE : MapEditorZoneDrag.SCALE,
            Sim(point), anchor);
    }

    private void UpdateDrag(Vector2 point)
    {
        point = ClampToMap(point);
        _interaction.Update(Sim(point));
        QueueRedraw();
    }

    private void EndDrag(Vector2 point)
    {
        point = ClampToMap(point);
        _interaction.Commit(Sim(point));
        QueueRedraw();
    }

    private void DrawZone(MapEditorZoneDraft zone, bool selected)
    {
        Color fill = selected ? _selectedFill : _zoneFill;
        Color line = selected ? _selectedLine : _zoneLine;
        if (zone.Shape is RectMapZoneShape rect)
        {
            Vector2[] points = RectPoints(rect);
            DrawShape(points, fill, line, selected);
            if (selected)
            {
                foreach (Vector2 point in points)
                {
                    DrawHandle(point, move: false);
                }

                DrawHandle(MapToLocal(new Vector2(
                    rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f)), move: true);
            }
            return;
        }
        Vector2 center;
        Vector2[] oval;
        Vector2 scaleHandle;
        if (zone.Shape is CircleMapZoneShape circle)
        {
            center = MapToLocal(new Vector2(circle.X, circle.Y));
            oval = EllipsePoints(circle.X, circle.Y, circle.Radius, circle.Radius, 0);
            scaleHandle = center + new Vector2(circle.Radius * Zoom, 0);
        }
        else
        {
            EllipseMapZoneShape ellipse = (EllipseMapZoneShape)zone.Shape;
            center = MapToLocal(new Vector2(ellipse.X, ellipse.Y));
            oval = EllipsePoints(ellipse.X, ellipse.Y, ellipse.RadiusX,
                ellipse.RadiusY, ellipse.Rotation);
            float rotation = Mathf.DegToRad(ellipse.Rotation);
            scaleHandle = center +
                (new Vector2(ellipse.RadiusX, ellipse.RadiusY) * Zoom).Rotated(rotation);
        }
        DrawShape(oval, fill, line, selected);
        if (selected)
        {
            DrawHandle(center, move: true);
            DrawHandle(scaleHandle, move: false);
        }
    }

    private void DrawSpawns()
    {
        foreach (MapEditorSpawn entry in _snapshot!.SpawnPoints)
        {
            MapSpawnPoint spawn = entry.Id == SelectedSpawnId &&
                _interaction.SpawnPreview is { } preview ? preview : entry.Value;
            Vector2 point = MapToLocal(new Vector2(spawn.X, spawn.Y));
            Vector2 bodySize = new(32f * Zoom, 32f * Zoom);
            Rect2 body = new(point - new Vector2(bodySize.X / 2f, bodySize.Y), bodySize);
            bool selected = entry.Id == SelectedSpawnId;
            DrawTextureRectRegion(_playerTexture, body, new Rect2(0, 0, 32, 32),
                SpawnColor(spawn.Team, selected ? 0.9f : 0.5f));
            if (selected)
                DrawRect(body, Colors.White, false, 1f);
        }
        if (_interaction.SpawnPreview is { } created && SelectedSpawnId == null)
            DrawSpawn(created, true);
    }

    private void DrawSpawn(MapSpawnPoint spawn, bool selected)
    {
        Vector2 point = MapToLocal(new Vector2(spawn.X, spawn.Y));
        Vector2 bodySize = new(32f * Zoom, 32f * Zoom);
        Rect2 body = new(point - new Vector2(bodySize.X / 2f, bodySize.Y), bodySize);
        DrawTextureRectRegion(_playerTexture, body, new Rect2(0, 0, 32, 32),
            SpawnColor(spawn.Team, selected ? 0.9f : 0.5f));
        if (selected)
            DrawRect(body, Colors.White, false, 1f);
    }

    private void DrawSpawnPreview()
    {
        Vector2 feet = MapToLocal(_cursorMapPosition);
        Vector2 bodySize = new(32f * Zoom, 32f * Zoom);
        Rect2 body = new(feet - new Vector2(bodySize.X / 2f, bodySize.Y), bodySize);
        DrawTextureRectRegion(_playerTexture, body, new Rect2(0, 0, 32, 32),
            new Color(1, 1, 1, 0.55f));
        DrawRect(body, new Color(1, 1, 1, 0.65f), false, 1f);
    }

    private static Color SpawnColor(Team? team, float alpha) => team switch
    {
        Team.BLUE => new Color(0.6f, 0.75f, 1f, alpha),
        Team.RED => new Color(1f, 0.6f, 0.6f, alpha),
        _ => new Color(1f, 1f, 1f, alpha),
    };

    private void DrawShape(Vector2[] points, Color fill, Color line, bool selected)
    {
        DrawColoredPolygon(points, fill);
        Vector2[] outline = new Vector2[points.Length + 1];
        points.CopyTo(outline, 0);
        outline[^1] = points[0];
        DrawPolyline(outline, line, selected ? 3f : 2f);
    }

    private Vector2[] RectPoints(RectMapZoneShape rect)
    {
        Vector2 center = new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        float rotation = Mathf.DegToRad(rect.Rotation);
        Vector2[] points =
        [
            new(rect.X, rect.Y),
            new(rect.X + rect.Width, rect.Y),
            new(rect.X + rect.Width, rect.Y + rect.Height),
            new(rect.X, rect.Y + rect.Height),
        ];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = MapToLocal(center + (points[i] - center).Rotated(rotation));
        }

        return points;
    }

    private Vector2[] EllipsePoints(int x, int y, int radiusX, int radiusY,
        float rotation)
    {
        const int SEGMENTS = 64;
        Vector2[] points = new Vector2[SEGMENTS];
        Vector2 center = new(x, y);
        float radians = Mathf.DegToRad(rotation);
        for (int i = 0; i < SEGMENTS; i++)
        {
            float angle = Mathf.Tau * i / SEGMENTS;
            Vector2 offset = new(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
            points[i] = MapToLocal(center + offset.Rotated(radians));
        }
        return points;
    }

    private void DrawHandle(Vector2 point, bool move)
    {
        Color color = move ? new Color(0.35f, 1f, 0.45f) : _selectedLine;
        DrawCircle(point, HANDLE_RADIUS, color);
        DrawCircle(point, HANDLE_RADIUS - 3f, new Color(0.08f, 0.09f, 0.1f));
    }

    private Rect2 MapRect() => new(Size / 2f - _cameraPosition * Zoom,
        new Vector2(_mapSize.X, _mapSize.Y) * Zoom);

    private Vector2 LocalToMap(Vector2 point)
    {
        return (point - MapRect().Position) / Zoom;
    }

    private Vector2 MapToLocal(Vector2 point) => MapRect().Position + point * Zoom;

    private Vector2 ClampToMap(Vector2 point) => new(
        Math.Clamp(point.X, 0, _mapSize.X), Math.Clamp(point.Y, 0, _mapSize.Y));

    private static SimVec2 Sim(Vector2 point) => new(point.X, point.Y);

    private void MoveCamera(Vector2 delta)
    {
        _cameraPosition += delta / Zoom;
        QueueRedraw();
    }

    private void OnResized() => QueueRedraw();

    private void SetZoom(float zoom, Vector2 anchor)
    {
        Vector2 mapAtAnchor = LocalToMap(anchor);
        Zoom = MapEditorGeometry.ClampZoom(zoom);
        _cameraPosition = mapAtAnchor - (anchor - Size / 2f) / Zoom;
        ZoomChanged?.Invoke(Zoom);
        QueueRedraw();
    }

    private void ReportCursor(Vector2 localPosition)
    {
        Vector2 point = LocalToMap(localPosition);
        _cursorMapPosition = point;
        _cursorVisible = true;
        CursorMoved?.Invoke((int)point.X, (int)point.Y);
        QueueRedraw();
        if (_snapshot == null)
            return;
        if ((Tool is MapEditorTool.SPAWN or MapEditorTool.SELECT) && ShowSpawns)
        {
            if (HitTestSpawn(point) != null)
            {
                MouseDefaultCursorShape = CursorShape.Drag;
                return;
            }
            if (Tool == MapEditorTool.SPAWN)
            {
                MouseDefaultCursorShape = CursorShape.Cross;
                return;
            }
        }
        MapEditorZone? selected = SelectedZone();
        if (Tool != MapEditorTool.SELECT || selected == null)
        {
            MouseDefaultCursorShape = Tool == MapEditorTool.SELECT
                ? CursorShape.Arrow
                : MouseDefaultCursorShape;
            return;
        }
        MapZoneHandle handle = MapEditorGeometry.PickHandle(
            selected.Shape, Sim(point), HANDLE_RADIUS / Zoom, out _);
        MouseDefaultCursorShape = handle switch
        {
            MapZoneHandle.MOVE => CursorShape.Drag,
            MapZoneHandle.SCALE => CursorShape.Cross,
            _ => CursorShape.Arrow,
        };
    }

    private string UniqueName()
    {
        HashSet<string> names = _snapshot!.Zones.Select(zone => zone.Name).ToHashSet();
        for (int n = 1; ; n++)
        {
            string name = $"zone-{n}";
            if (!names.Contains(name))
                return name;
        }
    }

    private MapEditorZone? SelectedZone() => SelectedZoneId is { } id
        ? _snapshot?.Zones.FirstOrDefault(zone => zone.Id == id)
        : null;

    private MapEditorZone? HitTestZone(Vector2 point)
    {
        if (_snapshot == null)
            return null;
        for (int i = _snapshot.Zones.Length - 1; i >= 0; i--)
        {
            MapEditorZone zone = _snapshot.Zones[i];
            if (zone.Shape.Compile().Contains(Sim(point)))
                return zone;
        }
        return null;
    }

    private MapEditorSpawn? HitTestSpawn(Vector2 point)
    {
        if (_snapshot == null)
            return null;
        MapSpawnPoint[] values = _snapshot.SpawnPoints.Select(spawn => spawn.Value).ToArray();
        int index = MapEditorGeometry.HitTestSpawn(values, Sim(point), 3f / Zoom);
        return index >= 0 ? _snapshot.SpawnPoints[index] : null;
    }

    private void OnZoneSelectionChanged(MapEditorZoneId? id)
    {
        SelectionChanged?.Invoke(id);
        QueueRedraw();
    }

    private void OnSpawnSelectionChanged(MapEditorSpawnId? id)
    {
        SpawnSelectionChanged?.Invoke(id);
        QueueRedraw();
    }

    private void OnZonePreviewChanged(MapEditorZoneDraft? preview)
    {
        ZonePreviewChanged?.Invoke(preview);
        QueueRedraw();
    }

    private void OnSpawnPreviewChanged(MapSpawnPoint? preview)
    {
        SpawnPreviewChanged?.Invoke(preview);
        QueueRedraw();
    }

    private static MapEditorZoneDraft Draft(MapEditorZone zone) =>
        new(zone.Name, zone.Tags, zone.Shape, zone.Effects);

    private static ImageTexture DecodeTexture(MapEditorLayers layers, MapEditorLayer layer)
    {
        MapEditorLayerAsset asset = layer switch
        {
            MapEditorLayer.BACKGROUND => layers.Background,
            MapEditorLayer.SOLID => layers.Solid,
            MapEditorLayer.DESTRUCTIBLE => layers.Destructible,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
        Image image = new();
        Error error = image.LoadPngFromBuffer(asset.Png.ToArray());
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not decode adopted map layer ({error}).");
        return ImageTexture.CreateFromImage(image);
    }
}

public static class MapEditorCanvasLayerPlan
{
    public static IReadOnlyList<MapEditorLayer> LayersToDecode(MapEditorChange change) =>
        change switch
        {
            MapEditorOpened or MapEditorReloaded =>
            [
                MapEditorLayer.BACKGROUND,
                MapEditorLayer.SOLID,
                MapEditorLayer.DESTRUCTIBLE,
            ],
            MapEditorLayerReplaced replaced => [replaced.Layer],
            _ => [],
        };
}
