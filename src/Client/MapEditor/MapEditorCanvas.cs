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

    private enum DragMode
    {
        NONE,
        MOVE,
        SCALE,
        CREATE_RECT,
        CREATE_CIRCLE,
        MOVE_SPAWN,
    }

    private static readonly Color _zoneFill = new(0.15f, 0.65f, 1f, 0.16f);
    private static readonly Color _zoneLine = new(0.25f, 0.8f, 1f, 0.9f);
    private static readonly Color _selectedFill = new(1f, 0.65f, 0.1f, 0.2f);
    private static readonly Color _selectedLine = new(1f, 0.75f, 0.2f, 1f);

    private ImageTexture? _background;
    private ImageTexture? _solid;
    private ImageTexture? _destructible;
    [Export] private Texture2D _playerTexture = null!;
    private MapEditorDocument? _document;
    private Vector2I _mapSize;
    private DragMode _dragMode;
    private Vector2 _dragStart;
    private MapZoneDef? _dragOriginal;
    private Vector2 _scaleAnchor;
    private MapSpawnPoint? _spawnDragOriginal;
    private Vector2 _cameraPosition;
    private Vector2 _cursorMapPosition;
    private bool _panning;
    private bool _cursorVisible;
    private float _zoom = 1f;

    public event Action<int>? SelectionChanged;
    public event Action<int>? SpawnSelectionChanged;
    public event Action? DocumentChanged;
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
                Select(-1);
            if (value != MapEditorTool.SPAWN)
                SelectSpawn(-1);
            QueueRedraw();
        }
    }
    public int SelectedIndex { get; private set; } = -1;
    public int SelectedSpawnIndex { get; private set; } = -1;
    public bool ShowBackground { get; set; } = true;
    public bool ShowSolid { get; set; } = true;
    public bool ShowDestructible { get; set; } = true;
    public bool ShowZones { get; set; } = true;
    public bool ShowSpawns { get; set; } = true;
    public float Zoom => _zoom;

    public override void _Ready()
    {
        Tool = MapEditorTool.SELECT;
        ClipContents = true;
        Resized += OnResized;
    }

    public override void _ExitTree() => Resized -= OnResized;

    public void SetMap(Image background, Image solid, Image destructible,
        MapEditorDocument document)
    {
        _background = ImageTexture.CreateFromImage(background);
        _solid = ImageTexture.CreateFromImage(solid);
        _destructible = ImageTexture.CreateFromImage(destructible);
        _mapSize = new Vector2I(background.GetWidth(), background.GetHeight());
        _cameraPosition = new Vector2(_mapSize.X / 2f, _mapSize.Y / 2f);
        _zoom = 1f;
        _document = document;
        Select(-1);
        SelectSpawn(-1);
        ZoomChanged?.Invoke(_zoom);
        QueueRedraw();
    }

    public void SetLayer(MapEditorLayer layer, Image image)
    {
        ImageTexture texture = ImageTexture.CreateFromImage(image);
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
        QueueRedraw();
    }

    public void Select(int index)
    {
        if (_document == null || index < 0 || index >= _document.Zones.Count)
            index = -1;
        SelectedIndex = index;
        if (index >= 0 && SelectedSpawnIndex >= 0)
        {
            SelectedSpawnIndex = -1;
            SpawnSelectionChanged?.Invoke(-1);
        }
        SelectionChanged?.Invoke(index);
        QueueRedraw();
    }

    public void SelectSpawn(int index)
    {
        if (_document == null || index < 0 || index >= _document.SpawnPoints.Count)
            index = -1;
        SelectedSpawnIndex = index;
        if (index >= 0 && SelectedIndex >= 0)
        {
            SelectedIndex = -1;
            SelectionChanged?.Invoke(-1);
        }
        SpawnSelectionChanged?.Invoke(index);
        QueueRedraw();
    }

    public void ReplaceSelected(MapZoneDef zone)
    {
        if (_document == null || SelectedIndex < 0)
            return;
        _document.Replace(SelectedIndex, zone);
        DocumentChanged?.Invoke();
        QueueRedraw();
    }

    public void DeleteSelected()
    {
        if (_document == null || SelectedIndex < 0)
            return;
        _document.RemoveAt(SelectedIndex);
        Select(-1);
        DocumentChanged?.Invoke();
    }

    public void ReplaceSelectedSpawn(MapSpawnPoint spawn)
    {
        if (_document == null || SelectedSpawnIndex < 0)
            return;
        _document.ReplaceSpawn(SelectedSpawnIndex, spawn);
        DocumentChanged?.Invoke();
        QueueRedraw();
    }

    public void DeleteSelectedSpawn()
    {
        if (_document == null || SelectedSpawnIndex < 0)
            return;
        _document.RemoveSpawnAt(SelectedSpawnIndex);
        SelectSpawn(-1);
        DocumentChanged?.Invoke();
    }

    public void ZoomIn() => SetZoom(_zoom * ZOOM_STEP, Size / 2f);
    public void ZoomOut() => SetZoom(_zoom / ZOOM_STEP, Size / 2f);
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
        _zoom = view.Zoom;
        ZoomChanged?.Invoke(_zoom);
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
        if (_document == null)
            return;

        if (ShowZones)
        {
            for (int i = 0; i < _document.Zones.Count; i++)
            {
                DrawZone(_document.Zones[i], i == SelectedIndex);
            }
        }
        if (ShowSpawns)
            DrawSpawns();
        if (Tool == MapEditorTool.SPAWN && _cursorVisible)
            DrawSpawnPreview();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_document == null || _mapSize.X == 0)
            return;
        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            float factor = wheel.ButtonIndex == MouseButton.WheelUp ? ZOOM_STEP : 1f / ZOOM_STEP;
            SetZoom(_zoom * factor, wheel.Position);
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle)
        {
            _panning = middle.Pressed;
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                if (!MapRect().HasPoint(button.Position))
                    return;
                BeginDrag(LocalToMap(button.Position));
            }
            else if (_dragMode != DragMode.NONE)
                EndDrag(LocalToMap(button.Position));
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion motion && _panning)
        {
            ReportCursor(motion.Position);
            MoveCamera(-motion.Relative);
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion dragMotion && _dragMode != DragMode.NONE)
        {
            ReportCursor(dragMotion.Position);
            UpdateDrag(LocalToMap(dragMotion.Position));
            AcceptEvent();
            return;
        }
        if (@event is InputEventMouseMotion hoverMotion)
            ReportCursor(hoverMotion.Position);
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
        _dragStart = ClampToMap(point);
        if (Tool == MapEditorTool.SPAWN)
        {
            int hit = MapEditorGeometry.HitTestSpawn(_document!.SpawnPoints,
                Sim(_dragStart), 3f / _zoom);
            if (hit < 0)
            {
                _document.AddSpawn(new MapSpawnPoint((int)_dragStart.X, (int)_dragStart.Y));
                hit = _document.SpawnPoints.Count - 1;
                DocumentChanged?.Invoke();
            }
            StartSpawnDrag(hit, _dragStart);
            return;
        }
        if (Tool == MapEditorTool.SELECT)
        {
            if (ShowSpawns)
            {
                int spawn = MapEditorGeometry.HitTestSpawn(_document!.SpawnPoints,
                    Sim(_dragStart), 3f / _zoom);
                if (spawn >= 0)
                {
                    StartSpawnDrag(spawn, _dragStart);
                    return;
                }
            }
            SelectSpawn(-1);
            if (SelectedIndex >= 0 && TryStartHandleDrag(
                    _document!.Zones[SelectedIndex], _dragStart))
                return;
            int hit = MapEditorGeometry.HitTest(_document!.Zones, Sim(_dragStart));
            Select(hit);
            if (hit < 0)
                return;
            _dragOriginal = _document!.Zones[hit];
            TryStartHandleDrag(_dragOriginal, _dragStart);
            return;
        }

        string name = UniqueName();
        MapZoneShape shape = Tool == MapEditorTool.RECT
            ? new RectMapZoneShape((int)_dragStart.X, (int)_dragStart.Y, 1, 1)
            : new EllipseMapZoneShape((int)_dragStart.X, (int)_dragStart.Y, 1, 1);
        _document!.Add(new MapZoneDef { Name = name, Shape = shape });
        Select(_document.Zones.Count - 1);
        _dragOriginal = _document.Zones[SelectedIndex];
        _dragMode = Tool == MapEditorTool.RECT
            ? DragMode.CREATE_RECT
            : DragMode.CREATE_CIRCLE;
        DocumentChanged?.Invoke();
    }

    private void StartSpawnDrag(int index, Vector2 grabPoint)
    {
        SelectSpawn(index);
        _spawnDragOriginal = _document!.SpawnPoints[index];
        _dragStart = grabPoint;
        _dragMode = DragMode.MOVE_SPAWN;
    }

    private bool TryStartHandleDrag(MapZoneDef zone, Vector2 point)
    {
        MapZoneHandle handle = MapEditorGeometry.PickHandle(zone.Shape,
            Sim(point), HANDLE_RADIUS, out SimVec2 anchor);
        if (handle == MapZoneHandle.NONE)
            return false;
        _dragOriginal = zone;
        _scaleAnchor = new Vector2(anchor.X, anchor.Y);
        _dragMode = handle == MapZoneHandle.MOVE ? DragMode.MOVE : DragMode.SCALE;
        return true;
    }

    private void UpdateDrag(Vector2 point)
    {
        if (_dragMode == DragMode.MOVE_SPAWN)
        {
            UpdateSpawnDrag(point);
            return;
        }
        point = ClampToMap(point);
        if (_document == null || SelectedIndex < 0 || _dragOriginal == null)
            return;
        MapZoneDef changed = _dragMode switch
        {
            DragMode.CREATE_RECT => _dragOriginal with
            {
                Shape = MapEditorGeometry.RectFromCorners(Sim(_dragStart), Sim(point)),
            },
            DragMode.CREATE_CIRCLE => _dragOriginal with
            {
                Shape = MapEditorGeometry.EllipseFromCenter(Sim(_dragStart), Sim(point)),
            },
            DragMode.SCALE => MapEditorGeometry.Scale(
                _dragOriginal, Sim(_scaleAnchor), Sim(point)),
            DragMode.MOVE => MapEditorGeometry.Move(
                _dragOriginal, Sim(point - _dragStart)),
            _ => _dragOriginal,
        };
        _document.Replace(SelectedIndex, changed);
        SelectionChanged?.Invoke(SelectedIndex);
        QueueRedraw();
    }

    private void EndDrag(Vector2 point)
    {
        if (_dragMode == DragMode.MOVE_SPAWN)
        {
            UpdateSpawnDrag(point);
            _dragMode = DragMode.NONE;
            _spawnDragOriginal = null;
            DocumentChanged?.Invoke();
            return;
        }
        UpdateDrag(point);
        _dragMode = DragMode.NONE;
        _dragOriginal = null;
        DocumentChanged?.Invoke();
    }

    private void UpdateSpawnDrag(Vector2 point)
    {
        if (_document == null || SelectedSpawnIndex < 0 || _spawnDragOriginal == null)
            return;
        _document.ReplaceSpawn(SelectedSpawnIndex,
            MapEditorGeometry.MoveSpawn(_spawnDragOriginal.Value, Sim(_dragStart), Sim(point),
                _mapSize.X, _mapSize.Y));
        SpawnSelectionChanged?.Invoke(SelectedSpawnIndex);
        QueueRedraw();
    }

    private void DrawZone(MapZoneDef zone, bool selected)
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
            scaleHandle = center + new Vector2(circle.Radius * _zoom, 0);
        }
        else
        {
            EllipseMapZoneShape ellipse = (EllipseMapZoneShape)zone.Shape;
            center = MapToLocal(new Vector2(ellipse.X, ellipse.Y));
            oval = EllipsePoints(ellipse.X, ellipse.Y, ellipse.RadiusX,
                ellipse.RadiusY, ellipse.Rotation);
            float rotation = Mathf.DegToRad(ellipse.Rotation);
            scaleHandle = center +
                (new Vector2(ellipse.RadiusX, ellipse.RadiusY) * _zoom).Rotated(rotation);
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
        for (int i = 0; i < _document!.SpawnPoints.Count; i++)
        {
            MapSpawnPoint spawn = _document.SpawnPoints[i];
            Vector2 point = MapToLocal(new Vector2(spawn.X, spawn.Y));
            Vector2 bodySize = new(32f * _zoom, 32f * _zoom);
            Rect2 body = new(point - new Vector2(bodySize.X / 2f, bodySize.Y), bodySize);
            bool selected = i == SelectedSpawnIndex;
            DrawTextureRectRegion(_playerTexture, body, new Rect2(0, 0, 32, 32),
                SpawnColor(spawn.Team, selected ? 0.9f : 0.5f));
            if (selected)
                DrawRect(body, Colors.White, false, 1f);
        }
    }

    private void DrawSpawnPreview()
    {
        Vector2 feet = MapToLocal(_cursorMapPosition);
        Vector2 bodySize = new(32f * _zoom, 32f * _zoom);
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

    private Rect2 MapRect() => new(Size / 2f - _cameraPosition * _zoom,
        new Vector2(_mapSize.X, _mapSize.Y) * _zoom);

    private Vector2 LocalToMap(Vector2 point)
    {
        return (point - MapRect().Position) / _zoom;
    }

    private Vector2 MapToLocal(Vector2 point) => MapRect().Position + point * _zoom;

    private Vector2 ClampToMap(Vector2 point) => new(
        Math.Clamp(point.X, 0, _mapSize.X), Math.Clamp(point.Y, 0, _mapSize.Y));

    private static SimVec2 Sim(Vector2 point) => new(point.X, point.Y);

    private void MoveCamera(Vector2 delta)
    {
        _cameraPosition += delta / _zoom;
        QueueRedraw();
    }

    private void OnResized() => QueueRedraw();

    private void SetZoom(float zoom, Vector2 anchor)
    {
        Vector2 mapAtAnchor = LocalToMap(anchor);
        _zoom = MapEditorGeometry.ClampZoom(zoom);
        _cameraPosition = mapAtAnchor - (anchor - Size / 2f) / _zoom;
        ZoomChanged?.Invoke(_zoom);
        QueueRedraw();
    }

    private void ReportCursor(Vector2 localPosition)
    {
        Vector2 point = LocalToMap(localPosition);
        _cursorMapPosition = point;
        _cursorVisible = true;
        CursorMoved?.Invoke((int)point.X, (int)point.Y);
        QueueRedraw();
        if (_document == null)
            return;
        if ((Tool is MapEditorTool.SPAWN or MapEditorTool.SELECT) && ShowSpawns)
        {
            int spawn = MapEditorGeometry.HitTestSpawn(_document.SpawnPoints,
                Sim(point), 3f / _zoom);
            if (spawn >= 0)
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
        if (Tool != MapEditorTool.SELECT || SelectedIndex < 0)
        {
            MouseDefaultCursorShape = Tool == MapEditorTool.SELECT
                ? CursorShape.Arrow
                : MouseDefaultCursorShape;
            return;
        }
        MapZoneHandle handle = MapEditorGeometry.PickHandle(
            _document.Zones[SelectedIndex].Shape, Sim(point), HANDLE_RADIUS / _zoom, out _);
        MouseDefaultCursorShape = handle switch
        {
            MapZoneHandle.MOVE => CursorShape.Drag,
            MapZoneHandle.SCALE => CursorShape.Cross,
            _ => CursorShape.Arrow,
        };
    }

    private string UniqueName()
    {
        HashSet<string> names = _document!.Zones.Select(zone => zone.Name).ToHashSet();
        for (int n = 1; ; n++)
        {
            string name = $"zone-{n}";
            if (!names.Contains(name))
                return name;
        }
    }
}
