using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net.Match;
using Mortz.Shared;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Match;

/// <summary>The loaded map on screen: layer sprites, collision mask, and carve events.</summary>
[Meta(typeof(IAutoNode))]
public partial class GameMap : Node2D
{
    private static readonly ILogger _log = MortzLog.For("client");

    private static readonly Color _hole = new(0, 0, 0, 0);

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
    public event Action<ImpactIdentity, Vector2, int>? Exploded;
    /// <summary>A carve removed ground; the pixels and their colors, for debris.</summary>
    public event Action<ImpactIdentity, Vector2, List<(Vector2 Position, Color Color)>>? GroundRemoved;

    public ClientTerrain Terrain { get; private set; } = null!;
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
    private readonly List<(ImpactIdentity Identity, List<(Vector2 Position, Color Color)> Pixels)>
        _recentCarves = [];
    private List<(Vector2 Position, Color Color)> _activeReplayPixels = [];

    /// <summary>Must be called right after instantiating, before entering the tree.</summary>
    public void Initialize(MapPackage map, ClientTerrain terrain)
    {
        Terrain = terrain;
        Mask = terrain.Mask;

        _pristineDestructible = map.Destructible;
        _destructibleImage = (Image)map.Destructible.Duplicate();
        for (int y = 0; y < Mask.Height; y++)
        {
            for (int x = 0; x < Mask.Width; x++)
            {
                if (!Mask.IsSolid(x, y))
                    _destructibleImage.SetPixel(x, y, _hole);
            }
        }
        _destructibleTexture = ImageTexture.CreateFromImage(_destructibleImage);
        _replayTerrainImage = Image.CreateEmpty(
            Mask.Width, Mask.Height, false, Image.Format.Rgba8);
        _replayTerrainTexture = ImageTexture.CreateFromImage(_replayTerrainImage);

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
        Terrain.Impact += PresentImpact;
        Terrain.Restored += RestorePixels;
    }

    public void OnExitTree()
    {
        if (Terrain == null)
            return;
        Terrain.Impact -= PresentImpact;
        Terrain.Restored -= RestorePixels;
    }

    private void PresentImpact(TerrainImpact impact)
    {
        Vector2 center = new(impact.X, impact.Y);
        if (impact.PlayEffects)
            Exploded?.Invoke(impact.Identity, center, impact.Radius);
        EraseLooseBlood(impact.X, impact.Y, impact.Radius);
        List<(Vector2 Position, Color Color)> debris = new(impact.Removed.Count);
        foreach ((int x, int y) in impact.Removed)
        {
            debris.Add((new Vector2(x, y), _destructibleImage.GetPixel(x, y)));
            _destructibleImage.SetPixel(x, y, _hole);
        }
        RememberCarve(impact.Identity, debris);
        _destructibleTexture.Update(_destructibleImage);
        if (impact.PlayEffects && debris.Count > 0)
            GroundRemoved?.Invoke(impact.Identity, center, debris);
    }

    private void RestorePixels(IReadOnlyList<(int X, int Y)> pixels)
    {
        foreach ((int x, int y) in pixels)
        {
            _destructibleImage.SetPixel(x, y, _pristineDestructible.GetPixel(x, y));
        }
        _destructibleTexture.Update(_destructibleImage);
    }

    /// <summary>Visually rebuild the pixels the winning blast removed; mask and
    /// image stay carved, only the overlay shows the pre-impact floor.</summary>
    public void BeginReplayTerrain(FinalKillMsg final)
    {
        EndReplayTerrain();
        if (!final.Flags.HasFlag(FinalKillFlags.EXPLOSION))
            return;
        int index = _recentCarves.FindLastIndex(
            carve => MatchEffects.IsDecisive(carve.Identity, final));
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
        ImpactIdentity identity, List<(Vector2 Position, Color Color)> pixels)
    {
        if (pixels.Count == 0)
            return;
        _recentCarves.Add((identity, pixels));
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

}
