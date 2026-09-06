using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Match;
using Mortz.Client.Replay;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Net.Sim;

namespace Mortz.Client.Effects;

/// <summary>
/// Spawns the one-shot cosmetic bursts (explosions, terrain debris, death
/// gibs) as its own children, keeping particles out of the terrain and player
/// nodes. The presentation controller chooses when decisive effects play.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class EffectsSpawner : Node2D
{
    private Node2D _liveEffects = null!;
    private Node2D _replayEffects = null!;
    private FinalKillMsg? _finalKill;
    private bool _subscribedToMap;
    private List<(Vector2 Position, Color Color)> _replayDebris = [];
    private (ImpactIdentity Identity, List<(Vector2 Position, Color Color)> Debris)? _recentDebris;

    [Dependency]
    private GameMap Map => this.DependOn<GameMap>();

    [Dependency]
    private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency]
    private ClientMatchRuntime Runtime => this.DependOn<ClientMatchRuntime>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _liveEffects = NewContainer("LiveEffects");
        _replayEffects = NewContainer("ReplayEffects");
    }

    public void OnResolved()
    {
        Runtime.Died += OnDeath;
        Map.Exploded += OnExploded;
        Map.GroundRemoved += OnGroundRemoved;
        _subscribedToMap = true;
    }

    public void OnExitTree()
    {
        if (!_subscribedToMap)
            return;
        Runtime.Died -= OnDeath;
        Map.Exploded -= OnExploded;
        Map.GroundRemoved -= OnGroundRemoved;
        _subscribedToMap = false;
    }

    private void OnExploded(ImpactIdentity identity, Vector2 center, int radius)
    {
        if (SuppressExplosion(identity))
            return;
        Sfx.PlayAt(Sfx.Sounds.ShellImpact, center);
        _liveEffects.AddChild(CarveBurst.Explosion(center, radius));
    }

    private void OnGroundRemoved(ImpactIdentity identity, Vector2 center, List<(Vector2 Position, Color Color)> debris)
    {
        _recentDebris = (identity, debris);
        if (SuppressExplosion(identity))
        {
            _replayDebris = debris;
            return;
        }
        _liveEffects.AddChild(CarveBurst.Create(center, debris));
    }

    private void OnDeath(DeathMsg msg)
    {
        if (_finalKill is FinalKillMsg final && msg.PeerId == final.VictimId &&
            msg.Tick == final.Tick && msg.ShellId == final.ShellId)
            return;
        Sfx.PlayAt(Sfx.Sounds.DeathScream, new Vector2(msg.X, msg.Y));
        _liveEffects.AddChild(GibBurst.Create(
            new Vector2(msg.X, msg.Y), Map.Mask, Map.Blood.Paint));
    }

    public void DeferDecisiveImpact(FinalKillMsg msg)
    {
        _finalKill = msg;
        _replayDebris = [];
        if (_recentDebris is (ImpactIdentity Identity, List<(Vector2 Position, Color Color)> Debris) recent &&
            MatchEffects.IsDecisive(recent.Identity, msg))
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
            explosion.PlaybackSpeed = ClientClock.TimeScale;
            _replayEffects.AddChild(explosion);
            if (_replayDebris.Count > 0)
            {
                CarveBurst debris = CarveBurst.Create(impact, _replayDebris);
                debris.PlaybackSpeed = ClientClock.TimeScale;
                _replayEffects.AddChild(debris);
            }
        }
        Sfx.PlayAt(Sfx.Sounds.DeathScream, death);
        GibBurst gibs = GibBurst.Create(death, Map.Mask, (_, _, _) => { });
        gibs.PlaybackSpeed = ClientClock.TimeScale;
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

    private bool SuppressExplosion(ImpactIdentity identity) =>
        _finalKill is FinalKillMsg final && MatchEffects.IsDecisive(identity, final);

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
