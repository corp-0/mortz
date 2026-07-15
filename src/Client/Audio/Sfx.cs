using Godot;

namespace Mortz.Client;

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
    private const int FLAT_PREWARM = 8;
    private const int FLAT_CAP = 16;
    private const int SPATIAL_PREWARM = 32;
    private const int SPATIAL_CAP = 160;

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

    [Export] private SoundRegistry _sounds = null!;

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
            AddVoice(spatial: false);
        for (int i = 0; i < SPATIAL_PREWARM; i++)
            AddVoice(spatial: true);
        ValidateRegistry();
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

    public static SfxHandle Play(SoundEffect sound) =>
        _instance?.Start(sound, spatial: false, default, null) ?? default;

    public static SfxHandle PlayAt(SoundEffect sound, Vector2 position) =>
        _instance?.Start(sound, spatial: true, position, null) ?? default;

    public static SfxHandle PlayAttached(SoundEffect sound, Node2D target) =>
        _instance?.Start(sound, spatial: true,
            GodotObject.IsInstanceValid(target) ? target.GlobalPosition : default, target) ?? default;

    internal void Stop(bool spatial, int index, uint generation) =>
        ReleaseVoice(spatial ? _spatial : _flat, index, generation, stop: true);

    private SfxHandle Start(SoundEffect sound, bool spatial, Vector2 position, Node2D? target)
    {
        if (!Valid(sound) || (target != null && !GodotObject.IsInstanceValid(target)))
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
        voice.BasePitch = 1f;
        voice.Target = target;
        ConfigurePlayer(voice.Player, sound, position,
            sound.TimeScaled ? CurrentTimeScale() : 1f);
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
            ReleaseVoice(pool, i, pool[i].Generation, stop: true);
    }

    private static void FollowTargets(List<Voice> pool)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            Voice voice = pool[i];
            if (!voice.Active || voice.Target == null)
                continue;
            if (!GodotObject.IsInstanceValid(voice.Target) || voice.Target.IsQueuedForDeletion())
            {
                ReleaseVoice(pool, i, voice.Generation, stop: true);
                continue;
            }
            ((AudioStreamPlayer2D)voice.Player).GlobalPosition = voice.Target.GlobalPosition;
        }
    }

    private static void ApplyTimeScale(List<Voice> pool, float scale)
    {
        foreach (Voice voice in pool)
            if (voice.Active && voice.TimeScaled)
                SetPitch(voice.Player, voice.BasePitch * scale);
    }

    private void ValidateRegistry()
    {
        List<string> errors = new();
        if (_sounds == null)
            errors.Add("SoundRegistry is missing");
        else
        {
            foreach ((string name, SoundEffect sound) in _sounds.Entries())
            {
                if (sound == null)
                    errors.Add($"{name}: definition is missing");
                else if (sound.Stream == null)
                    errors.Add($"{name}: stream is missing");
                else if (AudioServer.GetBusIndex(sound.BusName) < 0)
                    errors.Add($"{name}: bus '{sound.BusName}' does not exist");
            }
        }
        if (errors.Count > 0)
            GD.PushError("Invalid sound registry:\n  " + string.Join("\n  ", errors));
    }

    private static bool Valid(SoundEffect? sound) =>
        sound != null && GodotObject.IsInstanceValid(sound) && sound.Stream != null;

    private static float CurrentTimeScale() =>
        Mathf.Clamp((float)Engine.TimeScale, 0.05f, 1f);

    private static void ConfigurePlayer(Node player, SoundEffect sound, Vector2 position, float pitch)
    {
        switch (player)
        {
            case AudioStreamPlayer flat:
                flat.Stream = sound.Stream;
                flat.VolumeDb = sound.VolumeDb;
                flat.Bus = sound.BusName;
                flat.PitchScale = pitch;
                break;
            case AudioStreamPlayer2D spatial:
                spatial.Stream = sound.Stream;
                spatial.VolumeDb = sound.VolumeDb;
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
