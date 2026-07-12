using Godot;
using Mortz.Net;

namespace Mortz.Client;

/// <summary>
/// Spawns the one-shot cosmetic bursts (explosions, terrain debris, death
/// gibs) as its own children, keeping particles out of the terrain and player
/// nodes. Listens to GameMap for carve visuals and to the network for deaths.
/// </summary>
public partial class EffectsSpawner : Node2D
{
    [Export] private GameMap _gameMap = null!;

    public override void _Ready()
    {
        _gameMap.Exploded += OnExploded;
        _gameMap.GroundRemoved += OnGroundRemoved;
        NetworkManager.Instance.DeathReceived += OnDeathReceived;
    }

    public override void _ExitTree()
    {
        _gameMap.Exploded -= OnExploded;
        _gameMap.GroundRemoved -= OnGroundRemoved;
        NetworkManager.Instance.DeathReceived -= OnDeathReceived;
    }

    private void OnExploded(Vector2 center, int radius) =>
        AddChild(CarveBurst.Explosion(center, radius));

    private void OnGroundRemoved(Vector2 center, List<(Vector2 Position, Color Color)> debris) =>
        AddChild(CarveBurst.Create(center, debris));

    private void OnDeathReceived(long peerId, int x, int y) =>
        AddChild(GibBurst.Create(new Vector2(x, y), _gameMap.Mask, _gameMap.Blood.Paint));
}
