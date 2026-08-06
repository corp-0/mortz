using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Net;
using Mortz.Core.Net.Match;
using Mortz.Core.Net.Sim;
using Mortz.Core.Terrain;
using Mortz.Net;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;
using Combat = Mortz.Core.Match.Configuration.Combat;

namespace Mortz.Client.Match;

/// <summary>The loaded map on screen: layer sprites, collision mask, and carve events.</summary>
[Meta(typeof(IAutoNode))]
public partial class GameMap : Node2D, IHandle<CarveMsg>
{
    private static readonly ILogger _log = MortzLog.For("client");

    private static readonly Color _hole = new(0, 0, 0, 0);

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    [Dependency]
    private NetRouter Router => this.DependOn<NetRouter>();

    private NetRouter? _routed;

    public override void _Notification(int what) => this.Notify(what);

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
    private ZoneOverlay? _zoneOverlay;

    // Predicted carves use the match's radius; authoritative ones carry theirs.
    private int _carveRadius;

    // Working copy of the destructible layer, punched transparent by carves.
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
    public void Initialize(MapPackage map, Combat config,
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
        _log.Information("Terrain sync: {Pixels} px already removed", alreadyRemoved);

        _background.Texture = ImageTexture.CreateFromImage(map.Background);
        _solid.Texture = ImageTexture.CreateFromImage(map.Solid);
        _destructible.Texture = _destructibleTexture;
        _replayTerrain.Texture = _replayTerrainTexture;
        _blood.Initialize(Mask.Width, Mask.Height);

        if (map.Zones.All.Count > 0)
        {
            _zoneOverlay = new ZoneOverlay { Visible = false };
            _zoneOverlay.Initialize(map.Zones);
            AddChild(_zoneOverlay);
        }
    }

    /// <summary>Zones are debug/editor markup, not part of the normal map art.</summary>
    public void SetZonesVisible(bool visible)
    {
        if (_zoneOverlay != null)
            _zoneOverlay.Visible = visible;
    }

    public void OnResolved()
    {
        _routed = Router;
        _routed.Add(this);
    }

    public void OnExitTree()
    {
        _routed?.Remove(this);
        _routed = null;
    }

    public override void _Process(double delta)
    {
        foreach ((int seq, CarveLedger.PendingCarve pending) in _ledger.Expire(Time.GetTicksMsec()))
        {
            _log.Information("predicted carve seq {Seq} expired, reverting", seq);
            Restore(pending, confirmedX: 0, confirmedY: 0, confirmedRadius: -1);
        }
    }

    /// <summary>The hole happens now instead of a round trip later. Skipped if
    /// already pending or settled: carving twice would leave a hole the server
    /// never confirms.</summary>
    public void PredictCarve(int spawnSeq, Vector2 impact)
    {
        if (_ledger.IsPending(spawnSeq) || _ledger.IsSettled(spawnSeq))
            return;
        int x = (int)impact.X, y = (int)impact.Y;
        Exploded?.Invoke(new Vector2(x, y), _carveRadius);
        List<(int X, int Y)> removed = Carve(x, y, _carveRadius);
        _ledger.AddPending(spawnSeq, x, y, _carveRadius, removed, Time.GetTicksMsec());
    }

    /// <summary>A parry took over this shell; its carve broadcasts -1 and never
    /// confirms this seq, so revert now instead of on timeout. True if a
    /// pending carve was reverted.</summary>
    public bool RevertPredictedCarve(int spawnSeq)
    {
        _ledger.MarkSettled(spawnSeq, Time.GetTicksMsec());
        if (!_ledger.TryConfirm(spawnSeq, out CarveLedger.PendingCarve? pending))
            return false;
        _log.Information("predicted carve seq {Seq} deflected, reverting", spawnSeq);
        Restore(pending, confirmedX: 0, confirmedY: 0, confirmedRadius: -1);
        return true;
    }

    public void Handle(in CarveMsg msg)
    {
        (int x, int y, int radius) = (msg.X, msg.Y, msg.Radius);
        ulong now = Time.GetTicksMsec();
        _ledger.RecordConfirmed(x, y, radius, now);

        bool mine = msg.OwnerId == Network.LocalPeerId && msg.SpawnSeq >= 0;
        if (mine)
            _ledger.MarkSettled(msg.SpawnSeq, now);

        if (mine && _ledger.TryConfirm(msg.SpawnSeq, out CarveLedger.PendingCarve? pending))
        {
            // Already predicted; on a mispredict this moves the hole quietly.
            Restore(pending, x, y, radius);
            Carve(x, y, radius, withDebris: false);
            return;
        }

        Exploded?.Invoke(new Vector2(x, y), radius);
        Carve(x, y, radius);
    }

    /// <summary>Punch the hole into mask, art and blood; returns the removed pixels.</summary>
    private List<(int X, int Y)> Carve(int x, int y, int radius, bool withDebris = true)
    {
        List<(int X, int Y)> removed = Mask.CarveCircle(x, y, radius);
        _log.Information("carve at ({X},{Y}) removed {Pixels} px", x, y, removed.Count);
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

    /// <summary>Visually rebuild the pixels the winning blast removed; mask and
    /// image stay carved, only the overlay shows the pre-impact floor.</summary>
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

    /// <summary>Wipe blood off every blast cell with no ground left under it;
    /// stains on surviving rock stay.</summary>
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

    /// <summary>Give back pixels a predicted carve removed, where the ledger
    /// says no confirmed or live carve covers them.</summary>
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
