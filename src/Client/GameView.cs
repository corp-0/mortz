using Godot;
using Mortz.Core;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// Renders the networked world and sends local input. The local player is
/// client-side predicted (instant response, corrections eased in over a few
/// frames); remote players render from interpolated snapshots. Terrain draws
/// as the three map layers, with carve events punching holes into the
/// destructible layer and throwing pixel-colored debris.
/// </summary>
public partial class GameView : Node2D
{
    /// <summary>How fast reconciliation corrections blend away (per second).</summary>
    private const float CORRECTION_DECAY = 10f;

    private static readonly Color Hole = new(0, 0, 0, 0);

    private readonly SnapshotBuffer _snapshots = new();
    private readonly Dictionary<int, ColorRect> _playerRects = new();
    private readonly RopeOverlay _ropes = new() { Name = "RopeOverlay" };
    private TerrainMask _mask = null!;
    private Predictor _predictor = null!;
    private Image _destructibleImage = null!;
    private ImageTexture _destructibleTexture = null!;
    private Vector2 _correctionOffset;
    private float _renderTick = -1;
    private int _lastLoggedSecond = -1;
    private bool _testCarveSent;

    /// <summary>Must be called before adding to the tree.</summary>
    public void Initialize(MapPackage map, byte[] removedData)
    {
        _mask = map.BuildMask();
        _predictor = new Predictor(_mask);

        // Local working copy of the destructible layer; carves punch it transparent.
        _destructibleImage = (Image)map.Destructible.Duplicate();
        int alreadyRemoved = 0;
        _mask.ApplyRemoved(removedData, (x, y) =>
        {
            _destructibleImage.SetPixel(x, y, Hole);
            alreadyRemoved++;
        });
        _destructibleTexture = ImageTexture.CreateFromImage(_destructibleImage);
        GD.Print($"[client] late-join sync: {alreadyRemoved} px already removed");

        AddLayerSprite(ImageTexture.CreateFromImage(map.Background));
        AddLayerSprite(_destructibleTexture);
        AddLayerSprite(ImageTexture.CreateFromImage(map.Solid));
        AddChild(_ropes);
    }

    private void AddLayerSprite(Texture2D texture) =>
        AddChild(new Sprite2D { Texture = texture, Centered = false });

    public override void _Ready()
    {
        NetworkManager.Instance.SnapshotReceived += OnSnapshotReceived;
        NetworkManager.Instance.CarveReceived += OnCarveReceived;
    }

    public override void _ExitTree()
    {
        NetworkManager.Instance.SnapshotReceived -= OnSnapshotReceived;
        NetworkManager.Instance.CarveReceived -= OnCarveReceived;
    }

    // ---- terrain ----

    private void OnCarveReceived(int x, int y, int radius)
    {
        List<(int X, int Y)> removed = _mask.CarveCircle(x, y, radius);
        if (removed.Count == 0)
            return;

        List<(Vector2, Color)> debris = new List<(Vector2, Color)>(removed.Count);
        foreach ((int px, int py) in removed)
        {
            debris.Add((new Vector2(px, py), _destructibleImage.GetPixel(px, py)));
            _destructibleImage.SetPixel(px, py, Hole);
        }
        _destructibleTexture.Update(_destructibleImage);
        AddChild(CarveBurst.Create(new Vector2(x, y), debris));
        GD.Print($"[client] carve at ({x},{y}) removed {removed.Count} px");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Dev stand-in for the mortar until weapons exist: click to carve.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Vector2 pos = GetGlobalMousePosition();
            NetworkManager.Instance.RequestDebugCarve((int)pos.X, (int)pos.Y);
        }
    }

    // ---- state flow ----

    private void OnSnapshotReceived(byte[] data)
    {
        Snapshot snapshot = Snapshot.Deserialize(data);
        _snapshots.Add(snapshot);

        int localId = Multiplayer.GetUniqueId();
        foreach (PlayerState player in snapshot.Players)
        {
            if (player.PeerId != localId)
                continue;
            if (!_predictor.Initialized)
                GD.Print("[client] prediction initialized");
            Vec2 correction = _predictor.Reconcile(player, player.LastInputSeq);
            // Small disagreements ease in; teleport-scale ones (respawn after a
            // death pit) snap immediately instead of sliding across the map.
            if (correction.Length() > 150)
                _correctionOffset = Vector2.Zero;
            else
                _correctionOffset += new Vector2(correction.X, correction.Y);
            SendTestCarveOnce();
            break;
        }

        // Heartbeat log for headless E2E verification, once per sim-second.
        int second = snapshot.Tick / SimConfig.TICK_RATE;
        if (second != _lastLoggedSecond)
        {
            _lastLoggedSecond = second;
            GD.Print($"[client] snapshot tick {snapshot.Tick}, {snapshot.Players.Length} player(s)");
        }
    }

    /// <summary>Headless E2E hook: carve the first destructible spot in the map once.</summary>
    private void SendTestCarveOnce()
    {
        if (_testCarveSent || !CmdArgs.HasFlag("--test-carve"))
            return;
        _testCarveSent = true;
        for (int y = 0; y < _mask.Height; y++)
            for (int x = 0; x < _mask.Width; x++)
            {
                if (_mask.Get(x, y) == TerrainMaterial.Destructible)
                {
                    GD.Print($"[client] requesting test carve at ({x},{y})");
                    NetworkManager.Instance.RequestDebugCarve(x, y);
                    return;
                }
            }
    }

    public override void _PhysicsProcess(double delta)
    {
        InputButtons buttons = InputButtons.None;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left))
            buttons |= InputButtons.Left;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right))
            buttons |= InputButtons.Right;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))
            buttons |= InputButtons.Up;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))
            buttons |= InputButtons.Down;
        if (Input.IsPhysicalKeyPressed(Key.Space))
            buttons |= InputButtons.Jump;
        if (Input.IsPhysicalKeyPressed(Key.Shift))
            buttons |= InputButtons.Dash;
        if (Input.IsMouseButtonPressed(MouseButton.Right))
            buttons |= InputButtons.Rope;

        byte aim = 0;
        if (_predictor.Initialized)
        {
            Vector2 toMouse = GetGlobalMousePosition() - BodyCenter(_predictor.State.Position);
            if (toMouse.LengthSquared() > 1)
                aim = PlayerInput.AimFromVector(new Vec2(toMouse.X, toMouse.Y));
        }

        _predictor.LocalTick(new PlayerInput(buttons, aim));
        NetworkManager.Instance.SendInputs(
            InputPacket.Encode(_predictor.RecentInputs(NetConfig.INPUT_REDUNDANCY)));
    }

    public override void _Process(double delta)
    {
        if (_snapshots.NewestTick < 0)
            return;

        // Advance the render clock at sim speed and keep it anchored
        // InterpolationDelayTicks behind the newest snapshot, correcting drift.
        float target = _snapshots.NewestTick - NetConfig.INTERPOLATION_DELAY_TICKS;
        if (_renderTick < 0 || MathF.Abs(target - _renderTick) > SimConfig.TICK_RATE)
            _renderTick = target; // desynced (join, big hitch): snap
        else
            _renderTick += (float)delta * SimConfig.TICK_RATE + (target - _renderTick) * 0.05f;

        InterpolatedState? state = _snapshots.Sample(_renderTick);
        if (state == null)
            return;

        int localId = Multiplayer.GetUniqueId();
        HashSet<int> seen = new HashSet<int>();
        _ropes.Segments.Clear();

        foreach (RenderPlayer player in state.Players)
        {
            if (player.PeerId == localId)
                continue;
            seen.Add(player.PeerId);
            Vector2 feet = new Vector2(player.Position.X, player.Position.Y);
            PlaceRect(player.PeerId, feet);
            if (player.Rope != RopeMode.None)
                _ropes.Segments.Add((BodyCenter(player.Position), new Vector2(player.RopePoint.X, player.RopePoint.Y)));
        }

        if (_predictor.Initialized)
        {
            seen.Add(localId);
            _correctionOffset *= MathF.Max(0f, 1f - CORRECTION_DECAY * (float)delta);
            Vector2 pos = new Vector2(_predictor.State.Position.X, _predictor.State.Position.Y);
            PlaceRect(localId, pos + _correctionOffset);
            if (_predictor.State.Rope != RopeMode.None)
                _ropes.Segments.Add((
                    BodyCenter(_predictor.State.Position) + _correctionOffset,
                    new Vector2(_predictor.State.RopePoint.X, _predictor.State.RopePoint.Y)));
        }

        foreach ((int peerId, ColorRect? rect) in _playerRects)
        {
            if (!seen.Contains(peerId))
            {
                rect.QueueFree();
                _playerRects.Remove(peerId);
            }
        }
    }

    private void PlaceRect(int peerId, Vector2 feetPosition)
    {
        if (!_playerRects.TryGetValue(peerId, out ColorRect? rect))
        {
            rect = CreatePlayerRect(peerId);
            _playerRects[peerId] = rect;
        }
        rect.Position = new Vector2(
            feetPosition.X - SimConfig.PLAYER_HALF_WIDTH,
            feetPosition.Y - SimConfig.PLAYER_HALF_HEIGHT * 2); // state Y = feet
    }

    private ColorRect CreatePlayerRect(int peerId)
    {
        bool isLocal = peerId == Multiplayer.GetUniqueId();
        ColorRect rect = new ColorRect
        {
            Size = new Vector2(SimConfig.PLAYER_HALF_WIDTH * 2, SimConfig.PLAYER_HALF_HEIGHT * 2),
            Color = isLocal ? Colors.White : ColorForPeer(peerId),
        };
        AddChild(rect);
        return rect;
    }

    private static Color ColorForPeer(int peerId) =>
        Color.FromHsv((peerId * 0.61803f) % 1f, 0.75f, 0.95f);

    private static Vector2 BodyCenter(Vec2 feet) =>
        new(feet.X, feet.Y - SimConfig.PLAYER_HALF_HEIGHT);
}
