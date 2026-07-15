using Godot;
using Mortz.Core.Net.Messages;

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
        DeathMsg.Received += OnDeath;
    }

    public override void _ExitTree()
    {
        _gameMap.Exploded -= OnExploded;
        _gameMap.GroundRemoved -= OnGroundRemoved;
        DeathMsg.Received -= OnDeath;
    }

    private void OnExploded(Vector2 center, int radius)
    {
        Sfx.PlayAt(Sfx.Sounds.ShellImpact, center);
        AddChild(CarveBurst.Explosion(center, radius));
    }

    private void OnGroundRemoved(Vector2 center, List<(Vector2 Position, Color Color)> debris) =>
        AddChild(CarveBurst.Create(center, debris));

    private void OnDeath(DeathMsg msg)
    {
        Sfx.PlayAt(Sfx.Sounds.DeathScream, new Vector2(msg.X, msg.Y));
        AddChild(GibBurst.Create(new Vector2(msg.X, msg.Y), _gameMap.Mask, _gameMap.Blood.Paint));
    }
}
