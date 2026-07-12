using Godot;
using Mortz.Core;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client;

/// <summary>
/// The loaded map on screen: the three layer sprites, the collision mask, and
/// carve events punching holes and debris into the destructible layer.
/// GameView owns players and netcode; anything about the map itself lives here.
///
/// Destruction is predicted for the local player's shells: the hole (and the
/// boom) appear the instant the predicted shell lands, and the entry waits in
/// _pendingCarves until the authoritative carve with the same owner+spawnSeq
/// confirms it. On a mismatch the mispredicted pixels are restored from the
/// pristine map, except where another carve legitimately covers them; a
/// pending carve the server never confirms reverts fully on timeout.
/// </summary>
public partial class GameMap : Node2D
{
    private static readonly Color _hole = new(0, 0, 0, 0);

    private const ulong PENDING_TIMEOUT_MS = 2000;
    /// <summary>How long confirmed circles are remembered to guard restores.</summary>
    private const ulong CONFIRM_MEMORY_MS = 3000;

    [Export] private Sprite2D _background = null!;
    [Export] private Sprite2D _solid = null!;
    [Export] private Sprite2D _destructible = null!;

    /// <summary>Collision mask kept in lockstep with the server via carve events.</summary>
    public TerrainMask Mask { get; private set; } = null!;

    private record PendingCarve(int X, int Y, int Radius, List<(int X, int Y)> Removed, ulong Expiry);

    private readonly Dictionary<int, PendingCarve> _pendingCarves = new();
    private readonly List<(int X, int Y, int Radius, ulong Expiry)> _recentConfirmed = new();

    // Working copy of the destructible layer; carves punch it transparent.
    // The pristine original stays around to un-carve mispredictions.
    private Image _destructibleImage = null!;
    private Image _pristineDestructible = null!;
    private ImageTexture _destructibleTexture = null!;

    // Blood stains, painted by landing gib particles. A separate transparent
    // overlay above the terrain layers, so staining never fights the carve
    // bookkeeping; carves wipe it where they remove ground.
    private Image _bloodImage = null!;
    private ImageTexture _bloodTexture = null!;
    private bool _bloodDirty;

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(MapPackage map, byte[] removedData)
    {
        Mask = map.BuildMask();

        _pristineDestructible = map.Destructible;
        _destructibleImage = (Image)map.Destructible.Duplicate();
        int alreadyRemoved = 0;
        Mask.ApplyRemoved(removedData, (x, y) =>
        {
            _destructibleImage.SetPixel(x, y, _hole);
            alreadyRemoved++;
        });
        _destructibleTexture = ImageTexture.CreateFromImage(_destructibleImage);
        GD.Print($"[client] late-join sync: {alreadyRemoved} px already removed");

        SetLayer(_background, ImageTexture.CreateFromImage(map.Background));
        SetLayer(_destructible, _destructibleTexture);
        SetLayer(_solid, ImageTexture.CreateFromImage(map.Solid));

        _bloodImage = Image.CreateEmpty(Mask.Width, Mask.Height, false, Image.Format.Rgba8);
        _bloodTexture = ImageTexture.CreateFromImage(_bloodImage);
        Sprite2D blood = new Sprite2D();
        SetLayer(blood, _bloodTexture);
        AddChild(blood); // added last: draws above the terrain layers
    }

    private static void SetLayer(Sprite2D sprite, Texture2D texture)
    {
        sprite.Centered = false; // layers anchor at the map origin
        sprite.Texture = texture;
    }

    public override void _Ready()
    {
        NetworkManager.Instance.CarveReceived += OnCarveReceived;
        NetworkManager.Instance.DeathReceived += OnDeathReceived;
    }

    public override void _ExitTree()
    {
        NetworkManager.Instance.CarveReceived -= OnCarveReceived;
        NetworkManager.Instance.DeathReceived -= OnDeathReceived;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();
        _recentConfirmed.RemoveAll(c => c.Expiry < now);
        if (_pendingCarves.Count > 0)
        {
            foreach ((int seq, PendingCarve pending) in _pendingCarves)
            {
                if (pending.Expiry >= now)
                    continue;
                // The server never confirmed this shell (killed before the
                // input applied, drained input, ...): un-carve the whole thing.
                GD.Print($"[client] predicted carve seq {seq} expired, reverting");
                _pendingCarves.Remove(seq);
                Restore(pending, confirmedX: 0, confirmedY: 0, confirmedRadius: -1);
            }
        }

        // Batch blood into one texture upload per frame however much landed.
        if (!_bloodDirty)
            return;
        _bloodDirty = false;
        _bloodTexture.Update(_bloodImage);
    }

    /// <summary>
    /// Predicted destruction: the local player's shell landed, so the hole
    /// happens now instead of a round trip later. Deduped by spawnSeq because
    /// reconcile replays can re-report an impact.
    /// </summary>
    public void PredictCarve(int spawnSeq, Vector2 impact)
    {
        if (_pendingCarves.ContainsKey(spawnSeq))
            return;
        int x = (int)impact.X, y = (int)impact.Y;
        AddChild(CarveBurst.Explosion(new Vector2(x, y), SimConfig.MORTAR_CARVE_RADIUS));
        List<(int X, int Y)> removed = Carve(x, y, SimConfig.MORTAR_CARVE_RADIUS, withDebris: true);
        _pendingCarves[spawnSeq] = new PendingCarve(
            x, y, SimConfig.MORTAR_CARVE_RADIUS, removed, Time.GetTicksMsec() + PENDING_TIMEOUT_MS);
    }

    private void OnCarveReceived(int x, int y, int radius, int ownerId, int spawnSeq)
    {
        _recentConfirmed.Add((x, y, radius, Time.GetTicksMsec() + CONFIRM_MEMORY_MS));

        if (ownerId == Multiplayer.GetUniqueId() && _pendingCarves.Remove(spawnSeq, out PendingCarve? pending))
        {
            // Our shell, already predicted. Usually a perfect match and both
            // steps are no-ops; on a mispredict this moves the hole quietly.
            Restore(pending, x, y, radius);
            Carve(x, y, radius, withDebris: false);
            return;
        }

        // Someone else's explosion (or one we never predicted): boom shows
        // regardless of what the carve removed. Solid rock explodes too, it
        // just doesn't break.
        AddChild(CarveBurst.Explosion(new Vector2(x, y), radius));
        Carve(x, y, radius, withDebris: true);
    }

    /// <summary>Punch the hole into mask, art and blood; returns the removed pixels.</summary>
    private List<(int X, int Y)> Carve(int x, int y, int radius, bool withDebris)
    {
        List<(int X, int Y)> removed = Mask.CarveCircle(x, y, radius);
        GD.Print($"[client] carve at ({x},{y}) removed {removed.Count} px");
        if (removed.Count == 0)
            return removed;

        List<(Vector2, Color)> debris = new List<(Vector2, Color)>(removed.Count);
        foreach ((int px, int py) in removed)
        {
            debris.Add((new Vector2(px, py), _destructibleImage.GetPixel(px, py)));
            _destructibleImage.SetPixel(px, py, _hole);
            _bloodImage.SetPixel(px, py, _hole); // the ground took the stain with it
        }
        _destructibleTexture.Update(_destructibleImage);
        _bloodDirty = true;
        if (withDebris)
            AddChild(CarveBurst.Create(new Vector2(x, y), debris));
        return removed;
    }

    /// <summary>
    /// Give back the pixels a predicted carve removed, except where the
    /// confirmed circle (or any other live carve) says they're really gone.
    /// </summary>
    private void Restore(PendingCarve pending, int confirmedX, int confirmedY, int confirmedRadius)
    {
        bool dirty = false;
        foreach ((int px, int py) in pending.Removed)
        {
            if (InsideCircle(px, py, confirmedX, confirmedY, confirmedRadius))
                continue;
            if (CoveredByLiveCarve(px, py))
                continue;
            Mask.RestoreDestructible(px, py);
            _destructibleImage.SetPixel(px, py, _pristineDestructible.GetPixel(px, py));
            dirty = true;
        }
        if (dirty)
            _destructibleTexture.Update(_destructibleImage);
    }

    private bool CoveredByLiveCarve(int px, int py)
    {
        foreach ((int _, PendingCarve other) in _pendingCarves)
            if (InsideCircle(px, py, other.X, other.Y, other.Radius))
                return true;
        foreach ((int cx, int cy, int r, ulong _) in _recentConfirmed)
            if (InsideCircle(px, py, cx, cy, r))
                return true;
        return false;
    }

    private static bool InsideCircle(int px, int py, int cx, int cy, int radius)
    {
        int dx = px - cx, dy = py - cy;
        return radius >= 0 && dx * dx + dy * dy <= radius * radius;
    }

    private void OnDeathReceived(long peerId, int x, int y)
    {
        AddChild(GibBurst.Create(new Vector2(x, y), Mask, PaintBlood));
    }

    private void PaintBlood(int x, int y, Color color)
    {
        if (x < 0 || x >= Mask.Width || y < 0 || y >= Mask.Height)
            return;
        _bloodImage.SetPixel(x, y, color * 0.85f); // dried a shade darker than in flight
        _bloodDirty = true;
    }
}
