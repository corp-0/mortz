using Godot;
using Mortz.Client.Replay;
using Mortz.Extensions;
using Mortz.Shared;

namespace Mortz.Client.Audio;

public readonly struct SfxHandle
{
    private readonly Sfx? _owner;
    private readonly bool _spatial;
    private readonly int _index;
    private readonly uint _generation;

    internal SfxHandle(Sfx owner, bool spatial, int index, uint generation)
    {
        _owner = owner;
        _spatial = spatial;
        _index = index;
        _generation = generation;
    }

    public void Stop() => _owner?.Stop(_spatial, _index, _generation);
}

/// <summary>Sound-effect manager, one per client. Voices come from bounded
/// pools; a full pool steals the oldest voice of equal or lower priority, and
/// drops the sound if there is none.</summary>
public partial class Sfx : Node
{
    internal const int FLAT_PREWARM = 8;
    internal const int FLAT_CAP = 16;
    internal const int SPATIAL_PREWARM = 32;
    internal const int SPATIAL_CAP = 160;

    private sealed class Voice
    {
        public required Node Player;
        public bool Active;
        public uint Generation = 1;
        public ulong StartSerial;
        public SoundPriority Priority;
        public bool TimeScaled;
        public float BasePitch = 1f;
        public Node2D? Target;
    }

    [Export] private SoundRegistry? _sounds;

    private static Sfx? _instance;
    private readonly List<Voice> _flat = new();
    private readonly List<Voice> _spatial = new();
    private ulong _startSerial;
    private float _lastTimeScale = -1f;

    internal int FlatVoiceCount => _flat.Count;
    internal int SpatialVoiceCount => _spatial.Count;
    internal int ActiveFlatVoices => _flat.Count(v => v.Active);
    internal int ActiveSpatialVoices => _spatial.Count(v => v.Active);

    public static SoundRegistry Sounds =>
        _instance?._sounds ?? throw new InvalidOperationException("Sfx is not ready.");

    public override void _Ready()
    {
        if (_instance != null && _instance != this)
            GD.PushError("More than one Sfx manager entered the client tree.");
        _instance = this;
        ProcessPriority = 100;
        for (int i = 0; i < FLAT_PREWARM; i++)
        {
            AddVoice(spatial: false);
        }
        for (int i = 0; i < SPATIAL_PREWARM; i++)
        {
            AddVoice(spatial: true);
        }
    }

    public override void _ExitTree()
    {
        ReleaseAll(_flat);
        ReleaseAll(_spatial);
        if (_instance == this)
            _instance = null;
    }

    public override void _Process(double delta)
    {
        FollowTargets(_spatial);
        float scale = CurrentTimeScale();
        if (!Mathf.IsEqualApprox(scale, _lastTimeScale))
        {
            ApplyTimeScale(_flat, scale);
            ApplyTimeScale(_spatial, scale);
            _lastTimeScale = scale;
        }
    }

    public static SfxHandle Play(SoundEffect? sound, float pitch = 1f, float gainDb = 0f) =>
        _instance?.Start(sound, spatial: false, default, null, pitch, gainDb) ?? default;

    public static SfxHandle PlayAt(SoundEffect? sound, Vector2 position, float pitch = 1f,
        float gainDb = 0f) =>
        _instance?.Start(sound, spatial: true, position, null, pitch, gainDb) ?? default;

    public static SfxHandle PlayAttached(SoundEffect? sound, Node2D target, float pitch = 1f,
        float gainDb = 0f) =>
        _instance?.Start(sound, spatial: true,
            target.OrNull()?.GlobalPosition ?? default, target, pitch, gainDb) ?? default;

    internal void Stop(bool spatial, int index, uint generation) =>
        ReleaseVoice(spatial ? _spatial : _flat, index, generation, stop: true);

    private SfxHandle Start(SoundEffect? sound, bool spatial, Vector2 position, Node2D? target,
        float pitch, float gainDb)
    {
        sound = sound.OrNull();
        if (sound?.Stream == null || (target != null && target.OrNull() == null))
            return default;

        List<Voice> pool = spatial ? _spatial : _flat;
        int cap = spatial ? SPATIAL_CAP : FLAT_CAP;
        int index = Acquire(pool, cap, sound.Priority, spatial);
        if (index < 0)
            return default;

        Voice voice = pool[index];
        voice.Active = true;
        voice.StartSerial = ++_startSerial;
        voice.Priority = sound.Priority;
        voice.TimeScaled = sound.TimeScaled;
        voice.BasePitch = pitch;
        voice.Target = target;
        ConfigurePlayer(voice.Player, sound, position,
            sound.TimeScaled ? pitch * CurrentTimeScale() : pitch, gainDb);
        PlayPlayer(voice.Player);
        return new SfxHandle(this, spatial, index, voice.Generation);
    }

    private int Acquire(List<Voice> pool, int cap, SoundPriority incoming, bool spatial)
    {
        int free = pool.FindIndex(v => !v.Active);
        if (free >= 0)
            return free;
        if (pool.Count < cap)
        {
            AddVoice(spatial);
            return pool.Count - 1;
        }

        int candidate = -1;
        for (int i = 0; i < pool.Count; i++)
        {
            Voice voice = pool[i];
            if (voice.Priority > incoming)
                continue;
            if (candidate < 0 || voice.Priority < pool[candidate].Priority ||
                voice.Priority == pool[candidate].Priority &&
                voice.StartSerial < pool[candidate].StartSerial)
                candidate = i;
        }
        if (candidate >= 0)
            ReleaseVoice(pool, candidate, pool[candidate].Generation, stop: true);
        return candidate;
    }

    private void AddVoice(bool spatial)
    {
        List<Voice> pool = spatial ? _spatial : _flat;
        int index = pool.Count;
        if (spatial)
        {
            AudioStreamPlayer2D player = new() { Name = $"SpatialVoice{index}" };
            player.Finished += () => OnFinished(spatial: true, index);
            AddChild(player);
            pool.Add(new Voice { Player = player });
        }
        else
        {
            AudioStreamPlayer player = new() { Name = $"FlatVoice{index}" };
            player.Finished += () => OnFinished(spatial: false, index);
            AddChild(player);
            pool.Add(new Voice { Player = player });
        }
    }

    private void OnFinished(bool spatial, int index)
    {
        List<Voice> pool = spatial ? _spatial : _flat;
        if (index < pool.Count)
            ReleaseVoice(pool, index, pool[index].Generation, stop: false);
    }

    private static void ReleaseVoice(List<Voice> pool, int index, uint generation, bool stop)
    {
        if (index < 0 || index >= pool.Count)
            return;
        Voice voice = pool[index];
        if (!voice.Active || voice.Generation != generation)
            return;
        if (stop)
            StopPlayer(voice.Player);
        SetStream(voice.Player, null);
        voice.Target = null;
        voice.Active = false;
        voice.TimeScaled = false;
        voice.Generation++;
    }

    private static void ReleaseAll(List<Voice> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            ReleaseVoice(pool, i, pool[i].Generation, stop: true);
        }
    }

    private static void FollowTargets(List<Voice> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            Voice voice = pool[i];
            if (!voice.Active || voice.Target == null)
                continue;
            Node2D? target = voice.Target.OrNull();
            if (target == null || target.IsQueuedForDeletion())
            {
                ReleaseVoice(pool, i, voice.Generation, stop: true);
                continue;
            }
            ((AudioStreamPlayer2D)voice.Player).GlobalPosition = target.GlobalPosition;
        }
    }

    private static void ApplyTimeScale(List<Voice> pool, float scale)
    {
        foreach (Voice voice in pool)
        {
            if (voice.Active && voice.TimeScaled)
                SetPitch(voice.Player, voice.BasePitch * scale);
        }
    }

    private static float CurrentTimeScale() =>
        Mathf.Clamp(ClientClock.TimeScale, 0.05f, 1f);

    private static void ConfigurePlayer(Node player, SoundEffect sound, Vector2 position,
        float pitch, float gainDb)
    {
        switch (player)
        {
            case AudioStreamPlayer flat:
                flat.Stream = sound.Stream;
                flat.VolumeDb = sound.VolumeDb + gainDb;
                flat.Bus = sound.BusName;
                flat.PitchScale = pitch;
                break;
            case AudioStreamPlayer2D spatial:
                spatial.Stream = sound.Stream;
                spatial.VolumeDb = sound.VolumeDb + gainDb;
                spatial.Bus = sound.BusName;
                spatial.PitchScale = pitch;
                spatial.MaxDistance = sound.MaxDistance;
                spatial.GlobalPosition = position;
                break;
        }
    }

    private static void PlayPlayer(Node player)
    {
        if (player is AudioStreamPlayer flat) flat.Play();
        else ((AudioStreamPlayer2D)player).Play();
    }

    private static void StopPlayer(Node player)
    {
        if (player is AudioStreamPlayer flat) flat.Stop();
        else ((AudioStreamPlayer2D)player).Stop();
    }

    private static void SetStream(Node player, AudioStream? stream)
    {
        if (player is AudioStreamPlayer flat) flat.Stream = stream;
        else ((AudioStreamPlayer2D)player).Stream = stream;
    }

    private static void SetPitch(Node player, float pitch)
    {
        if (player is AudioStreamPlayer flat) flat.PitchScale = pitch;
        else ((AudioStreamPlayer2D)player).PitchScale = pitch;
    }
}
