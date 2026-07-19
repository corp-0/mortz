using Godot;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;

namespace Mortz.Client.Match;

/// <summary>
/// The loaded map on screen: the three layer sprites, the collision mask, and
/// carve events punching holes into the destructible layer.
///
/// Destruction is predicted for the local player's shells: the hole appears
/// the instant the predicted shell lands, and the CarveLedger holds the entry
/// until the authoritative carve with the same owner+spawnSeq confirms it. On
/// a mismatch the mispredicted pixels are restored from the pristine map,
/// except where another carve legitimately covers them; a pending carve the
/// server never confirms reverts fully on timeout.
/// </summary>
public partial class GameMap : Node2D
{
    private static readonly Color _hole = new(0, 0, 0, 0);

    [Export] private Sprite2D _background = null!;
    [Export] private Sprite2D _solid = null!;
    [Export] private Sprite2D _destructible = null!;
    [Export] private Sprite2D _replayTerrain = null!;
    [Export] private BloodOverlay _blood = null!;

    /// <summary>Collision mask kept in lockstep with the server via carve events.</summary>
    public TerrainMask Mask { get; private set; } = null!;
    public BloodOverlay Blood => _blood;

    /// <summary>An explosion went off, carve or not. Solid rock explodes too,
    /// it just doesn't break.</summary>
    public event Action<Vector2, int>? Exploded;
    /// <summary>A carve removed ground; the pixels and their colors, for debris.</summary>
    public event Action<Vector2, List<(Vector2 Position, Color Color)>>? GroundRemoved;

    private readonly CarveLedger _ledger = new();

    // Predicted carves use the match's radius; authoritative ones carry theirs.
    private int _carveRadius;

    // Working copy of the destructible layer; carves punch it transparent.
    // The pristine original stays around to un-carve mispredictions.
    private Image _destructibleImage = null!;
    private Image _pristineDestructible = null!;
    private ImageTexture _destructibleTexture = null!;
    private Image _replayTerrainImage = null!;
    private ImageTexture _replayTerrainTexture = null!;
    private readonly List<(Vector2 Center, List<(Vector2 Position, Color Color)> Pixels)>
        _recentCarves = [];
    private List<(Vector2 Position, Color Color)> _activeReplayPixels = [];

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(MapPackage map, MatchConfig config,
        TerrainSyncEncoding terrainEncoding, byte[] terrainData)
    {
        Mask = map.BuildMask();
        _carveRadius = config.MortarCarveRadius;

        _pristineDestructible = map.Destructible;
        _destructibleImage = (Image)map.Destructible.Duplicate();
        int alreadyRemoved = 0;
        TerrainSync.Apply(Mask, terrainEncoding, terrainData, (x, y) =>
        {
            _destructibleImage.SetPixel(x, y, _hole);
            alreadyRemoved++;
        });
        _destructibleTexture = ImageTexture.CreateFromImage(_destructibleImage);
        _replayTerrainImage = Image.CreateEmpty(
            Mask.Width, Mask.Height, false, Image.Format.Rgba8);
        _replayTerrainTexture = ImageTexture.CreateFromImage(_replayTerrainImage);
        GD.Print($"[client] late-join sync: {alreadyRemoved} px already removed");

        _background.Texture = ImageTexture.CreateFromImage(map.Background);
        _solid.Texture = ImageTexture.CreateFromImage(map.Solid);
        _destructible.Texture = _destructibleTexture;
        _replayTerrain.Texture = _replayTerrainTexture;
        _blood.Initialize(Mask.Width, Mask.Height);
    }

    public override void _Ready() =>
        CarveMsg.Received += OnCarve;

    public override void _ExitTree() =>
        CarveMsg.Received -= OnCarve;

    public override void _Process(double delta)
    {
        foreach ((int seq, CarveLedger.PendingCarve pending) in _ledger.Expire(Time.GetTicksMsec()))
        {
            GD.Print($"[client] predicted carve seq {seq} expired, reverting");
            Restore(pending, confirmedX: 0, confirmedY: 0, confirmedRadius: -1);
        }
    }

    /// <summary>
    /// Predicted destruction: the shell landed, so the hole happens now instead
    /// of a round trip later. Skipped if the shot is already pending or settled;
    /// carving it twice would leave a hole the server never confirms.
    /// </summary>
    public void PredictCarve(int spawnSeq, Vector2 impact)
    {
        if (_ledger.IsPending(spawnSeq) || _ledger.IsSettled(spawnSeq))
            return;
        int x = (int)impact.X, y = (int)impact.Y;
        Exploded?.Invoke(new Vector2(x, y), _carveRadius);
        List<(int X, int Y)> removed = Carve(x, y, _carveRadius);
        _ledger.AddPending(spawnSeq, x, y, _carveRadius, removed, Time.GetTicksMsec());
    }

    /// <summary>A parry took over the shell that made this predicted carve. Revert
    /// it now instead of waiting for the timeout: the deflected shell's carve is
    /// -1 and never confirms this seq. Returns true if a pending carve was reverted.</summary>
    public bool RevertPredictedCarve(int spawnSeq)
    {
        _ledger.MarkSettled(spawnSeq, Time.GetTicksMsec());
        if (!_ledger.TryConfirm(spawnSeq, out CarveLedger.PendingCarve? pending))
            return false;
        GD.Print($"[client] predicted carve seq {spawnSeq} deflected, reverting");
        Restore(pending, confirmedX: 0, confirmedY: 0, confirmedRadius: -1);
        return true;
    }

    private void OnCarve(CarveMsg msg)
    {
        (int x, int y, int radius) = (msg.X, msg.Y, msg.Radius);
        ulong now = Time.GetTicksMsec();
        _ledger.RecordConfirmed(x, y, radius, now);

        bool mine = msg.OwnerId == NetworkManager.Instance.LocalPeerId && msg.SpawnSeq >= 0;
        if (mine)
            _ledger.MarkSettled(msg.SpawnSeq, now);

        if (mine && _ledger.TryConfirm(msg.SpawnSeq, out CarveLedger.PendingCarve? pending))
        {
            // Our shell, already predicted (boom included). Usually a perfect
            // match and both steps are no-ops; on a mispredict this moves the
            // hole quietly.
            Restore(pending, x, y, radius);
            Carve(x, y, radius, withDebris: false);
            return;
        }

        // Someone else's explosion (or one we never predicted).
        Exploded?.Invoke(new Vector2(x, y), radius);
        Carve(x, y, radius);
    }

    /// <summary>Punch the hole into mask, art and blood; returns the removed pixels.</summary>
    private List<(int X, int Y)> Carve(int x, int y, int radius, bool withDebris = true)
    {
        List<(int X, int Y)> removed = Mask.CarveCircle(x, y, radius);
        GD.Print($"[client] carve at ({x},{y}) removed {removed.Count} px");
        EraseLooseBlood(x, y, radius);
        if (removed.Count == 0)
            return removed;

        List<(Vector2 Position, Color Color)> debris = new(removed.Count);
        foreach ((int px, int py) in removed)
        {
            debris.Add((new Vector2(px, py), _destructibleImage.GetPixel(px, py)));
            _destructibleImage.SetPixel(px, py, _hole);
        }
        RememberCarve(new Vector2(x, y), debris);
        _destructibleTexture.Update(_destructibleImage);
        if (withDebris)
            GroundRemoved?.Invoke(new Vector2(x, y), debris);
        return removed;
    }

    /// <summary>Visually rebuild just the pixels removed by the winning blast.
    /// The real image and collision mask remain carved; recorded actors therefore
    /// see the pre-impact floor without gameplay state being rolled back.</summary>
    public void BeginReplayTerrain(FinalKillMsg final)
    {
        EndReplayTerrain();
        if (!final.Flags.HasFlag(FinalKillFlags.EXPLOSION))
            return;
        Vector2 impact = new(final.ImpactX, final.ImpactY);
        int index = _recentCarves.FindLastIndex(
            carve => carve.Center.DistanceSquaredTo(impact) <= 4f);
        if (index < 0)
            return;

        _activeReplayPixels = _recentCarves[index].Pixels;
        foreach ((Vector2 position, Color color) in _activeReplayPixels)
        {
            _replayTerrainImage.SetPixel((int)position.X, (int)position.Y, color);
        }
        _replayTerrainTexture.Update(_replayTerrainImage);
        _replayTerrain.Visible = true;
    }

    /// <summary>The replay reached the authoritative impact: reveal the real
    /// carved terrain underneath the temporary pre-impact pixels.</summary>
    public void ShowReplayImpact() => _replayTerrain.Visible = false;

    public void EndReplayTerrain()
    {
        _replayTerrain.Visible = false;
        if (_activeReplayPixels.Count == 0)
            return;
        foreach ((Vector2 position, Color _) in _activeReplayPixels)
        {
            _replayTerrainImage.SetPixel((int)position.X, (int)position.Y, _hole);
        }
        _replayTerrainTexture.Update(_replayTerrainImage);
        _activeReplayPixels = [];
    }

    private void RememberCarve(
        Vector2 center, List<(Vector2 Position, Color Color)> pixels)
    {
        if (pixels.Count == 0)
            return;
        _recentCarves.Add((center, pixels));
        if (_recentCarves.Count > 16)
            _recentCarves.RemoveAt(0);
    }

    /// <summary>
    /// The ground takes its stains with it: after the carve, wipe blood off
    /// every cell in the blast with no ground left under it, including stains
    /// hanging over older holes. Stains on surviving solid rock stay.
    /// </summary>
    private void EraseLooseBlood(int x, int y, int radius)
    {
        int r2 = radius * radius;
        for (int py = y - radius; py <= y + radius; py++)
        {
            for (int px = x - radius; px <= x + radius; px++)
            {
                int dx = px - x, dy = py - y;
                if (dx * dx + dy * dy <= r2 && !Mask.IsSolid(px, py))
                    _blood.Erase(px, py);
            }
        }
    }

    /// <summary>
    /// Give back the pixels a predicted carve removed, where the ledger says
    /// no confirmed or live carve really covers them.
    /// </summary>
    private void Restore(CarveLedger.PendingCarve pending, int confirmedX, int confirmedY, int confirmedRadius)
    {
        bool dirty = false;
        foreach ((int px, int py) in pending.Removed)
        {
            if (!_ledger.ShouldRestore(px, py, confirmedX, confirmedY, confirmedRadius))
                continue;
            Mask.RestoreDestructible(px, py);
            _destructibleImage.SetPixel(px, py, _pristineDestructible.GetPixel(px, py));
            dirty = true;
        }
        if (dirty)
            _destructibleTexture.Update(_destructibleImage);
    }
}
