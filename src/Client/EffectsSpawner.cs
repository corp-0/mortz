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

    private Node2D _liveEffects = null!;
    private Node2D _replayEffects = null!;
    private FinalKillMsg? _finalKill;
    private List<(Vector2 Position, Color Color)> _replayDebris = [];
    private (Vector2 Center, List<(Vector2 Position, Color Color)> Debris)? _recentDebris;

    public override void _Ready()
    {
        _liveEffects = NewContainer("LiveEffects");
        _replayEffects = NewContainer("ReplayEffects");
        _gameMap.Exploded += OnExploded;
        _gameMap.GroundRemoved += OnGroundRemoved;
        DeathMsg.Received += OnDeath;
        FinalKillMsg.Received += OnFinalKill;
    }

    public override void _ExitTree()
    {
        _gameMap.Exploded -= OnExploded;
        _gameMap.GroundRemoved -= OnGroundRemoved;
        DeathMsg.Received -= OnDeath;
        FinalKillMsg.Received -= OnFinalKill;
    }

    private void OnExploded(Vector2 center, int radius)
    {
        if (SuppressExplosion(center))
            return;
        Sfx.PlayAt(Sfx.Sounds.ShellImpact, center);
        _liveEffects.AddChild(CarveBurst.Explosion(center, radius));
    }

    private void OnGroundRemoved(Vector2 center, List<(Vector2 Position, Color Color)> debris)
    {
        _recentDebris = (center, debris);
        if (SuppressExplosion(center))
        {
            _replayDebris = debris;
            return;
        }
        _liveEffects.AddChild(CarveBurst.Create(center, debris));
    }

    private void OnDeath(DeathMsg msg)
    {
        if (_finalKill is { } final && msg.PeerId == final.VictimId)
            return;
        Sfx.PlayAt(Sfx.Sounds.DeathScream, new Vector2(msg.X, msg.Y));
        _liveEffects.AddChild(GibBurst.Create(
            new Vector2(msg.X, msg.Y), _gameMap.Mask, _gameMap.Blood.Paint));
    }

    private void OnFinalKill(FinalKillMsg msg)
    {
        _finalKill = msg;
        _replayDebris = [];
        if (_recentDebris is { } recent &&
            recent.Center.DistanceSquaredTo(new Vector2(msg.ImpactX, msg.ImpactY)) <= 4f)
            _replayDebris = recent.Debris;
    }

    public void BeginReplay()
    {
        ReplaceContainer(ref _liveEffects, "LiveEffects");
        ClearReplayPass();
    }

    private void ClearReplayPass() => ReplaceContainer(ref _replayEffects, "ReplayEffects");

    public void PlayReplayImpact(FinalKillMsg final)
    {
        Vector2 death = new(final.DeathX, final.DeathY);
        if (final.Flags.HasFlag(FinalKillFlags.EXPLOSION))
        {
            Vector2 impact = new(final.ImpactX, final.ImpactY);
            Sfx.PlayAt(Sfx.Sounds.ShellImpact, impact);
            CarveBurst explosion = CarveBurst.Explosion(impact, final.BlastRadius);
            explosion.PlaybackSpeed = 0.3f;
            _replayEffects.AddChild(explosion);
            if (_replayDebris.Count > 0)
            {
                CarveBurst debris = CarveBurst.Create(impact, _replayDebris);
                debris.PlaybackSpeed = 0.3f;
                _replayEffects.AddChild(debris);
            }
        }
        Sfx.PlayAt(Sfx.Sounds.DeathScream, death);
        GibBurst gibs = GibBurst.Create(death, _gameMap.Mask, (_, _, _) => { });
        gibs.PlaybackSpeed = 0.3f;
        _replayEffects.AddChild(gibs);
    }

    public void EndReplay()
    {
        // Keep the impact visible after the normal camera is restored; normal
        // node cleanup owns the remaining cosmetic particles.
        _finalKill = null;
        _replayDebris = [];
    }

    public void PlayWithoutReplay(FinalKillMsg final)
    {
        ClearReplayPass();
        PlayReplayImpact(final);
        _finalKill = null;
    }

    private bool SuppressExplosion(Vector2 center) =>
        _finalKill is { } final && final.Flags.HasFlag(FinalKillFlags.EXPLOSION) &&
        center.DistanceSquaredTo(new Vector2(final.ImpactX, final.ImpactY)) <= 4f;

    private Node2D NewContainer(string name)
    {
        Node2D container = new() { Name = name };
        AddChild(container);
        return container;
    }

    private void ReplaceContainer(ref Node2D container, string name)
    {
        container.Visible = false;
        container.QueueFree();
        container = NewContainer(name);
    }
}
