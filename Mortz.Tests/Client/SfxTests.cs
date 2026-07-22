using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Replay;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class SfxTests(MortzGodotFixture godot)
{
    [Fact]
    public void PrewarmsAndManualStopRecyclesExactlyOnce()
    {
        using Fixture f = new(godot.Tree);
        Assert.Equal(Sfx.FLAT_PREWARM, f.Manager.FlatVoiceCount);
        Assert.Equal(Sfx.SPATIAL_PREWARM, f.Manager.SpatialVoiceCount);

        SfxHandle handle = Sfx.Play(f.Gameplay);
        Assert.Equal(1, f.Manager.ActiveFlatVoices);
        handle.Stop();
        handle.Stop();
        Assert.Equal(0, f.Manager.ActiveFlatVoices);
        Assert.Equal(Sfx.FLAT_PREWARM, f.Manager.FlatVoiceCount);
    }

    [Fact]
    public void NaturalFinishAndInvalidTargetRecycleVoices()
    {
        using Fixture f = new(godot.Tree);
        Sfx.Play(f.Gameplay);
        AudioStreamPlayer player = Assert.IsType<AudioStreamPlayer>(f.Manager.GetChild(0));
        player.EmitSignal(AudioStreamPlayer.SignalName.Finished);
        Assert.Equal(0, f.Manager.ActiveFlatVoices);

        Node2D target = new();
        Sfx.PlayAttached(f.Gameplay, target);
        Assert.Equal(1, f.Manager.ActiveSpatialVoices);
        target.Free();
        f.Manager._Process(0);
        Assert.Equal(0, f.Manager.ActiveSpatialVoices);
    }

    [Fact]
    public void FlatPoolGrowsToCapAndStealsOldestEligibleVoice()
    {
        using Fixture f = new(godot.Tree);
        SfxHandle oldest = default;
        for (int i = 0; i < Sfx.FLAT_CAP; i++)
        {
            SfxHandle handle = Sfx.Play(f.Gameplay);
            if (i == 0) oldest = handle;
        }
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.FlatVoiceCount);
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);

        SfxHandle critical = Sfx.Play(f.Critical);
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);
        oldest.Stop(); // stale after the steal
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);
        critical.Stop();
        Assert.Equal(Sfx.FLAT_CAP - 1, f.Manager.ActiveFlatVoices);
    }

    [Fact]
    public void LowerPriorityRequestCannotStealCriticalVoice()
    {
        using Fixture f = new(godot.Tree);
        for (int i = 0; i < Sfx.FLAT_CAP; i++)
        {
            Sfx.Play(f.Critical);
        }

        SfxHandle dropped = Sfx.Play(f.Gameplay);
        dropped.Stop();
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.FlatVoiceCount);
    }

    [Fact]
    public void SpatialPoolGrowsToItsSimulationSizedCap()
    {
        using Fixture f = new(godot.Tree);
        for (int i = 0; i < Sfx.SPATIAL_CAP + 1; i++)
        {
            Sfx.PlayAt(f.Gameplay, new Vector2(i, 0));
        }
        Assert.Equal(Sfx.SPATIAL_CAP, f.Manager.SpatialVoiceCount);
        Assert.Equal(Sfx.SPATIAL_CAP, f.Manager.ActiveSpatialVoices);
    }

    [Fact]
    public void MissingStreamAndDefaultHandleAreSafeNoOps()
    {
        using Fixture f = new(godot.Tree);
        using SoundEffect missing = new();
        Sfx.Play(missing);
        default(SfxHandle).Stop();
        Assert.Equal(0, f.Manager.ActiveFlatVoices);
    }

    [Fact]
    public void TimeScaledSoundsFollowClientClockWhileAnnouncementsDoNot()
    {
        using Fixture f = new(godot.Tree);

        SfxHandle scaled = Sfx.Play(f.Gameplay);
        AudioStreamPlayer scaledPlayer = Assert.IsType<AudioStreamPlayer>(f.Manager.GetChild(0));
        ClientClock.BeginReplay();
        f.Manager._Process(0);
        Assert.Equal(ClientClock.REPLAY_TIME_SCALE, scaledPlayer.PitchScale, precision: 3);
        scaled.Stop();

        SfxHandle announcement = Sfx.Play(f.Critical);
        AudioStreamPlayer announcementPlayer = Assert.IsType<AudioStreamPlayer>(f.Manager.GetChild(0));
        Assert.Equal(1f, announcementPlayer.PitchScale, precision: 3);
        announcement.Stop();

        ClientClock.Reset();
        f.Manager._Process(0);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly AudioStreamWav _stream = new()
        {
            Data = [0, 0],
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = 44100,
        };

        public Sfx Manager { get; } = new();
        public SoundEffect Gameplay { get; } = new();
        public SoundEffect Critical { get; } = new();

        public Fixture(SceneTree tree)
        {
            Gameplay.Set("Stream", _stream);
            Critical.Set("Stream", _stream);
            Critical.Set("Priority", (int)SoundPriority.CRITICAL);
            Critical.Set("TimeScaled", false);
            tree.Root.AddChild(Manager);
        }

        public void Dispose()
        {
            ClientClock.Reset();
            Manager.GetParent()?.RemoveChild(Manager);
            Manager.Free();
            Gameplay.Dispose();
            Critical.Dispose();
            _stream.Dispose();
        }
    }
}
