using Godot;
using Mortz.Client;
using Mortz.Client.Audio;
using Mortz.Client.Replay;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class SfxTests
{
    [Fact]
    public void PrewarmsAndManualStopRecyclesExactlyOnce()
    {
        using Fixture f = new();
        Assert.Equal(Sfx.FLAT_PREWARM, f.Manager.FlatVoiceCount);
        Assert.Equal(Sfx.SPATIAL_PREWARM, f.Manager.SpatialVoiceCount);

        SfxHandle handle = Sfx.Play(f.Sounds.ShellWhoosh);
        Assert.Equal(1, f.Manager.ActiveFlatVoices);
        handle.Stop();
        handle.Stop();
        Assert.Equal(0, f.Manager.ActiveFlatVoices);
        Assert.Equal(Sfx.FLAT_PREWARM, f.Manager.FlatVoiceCount);
    }

    [Fact]
    public void NaturalFinishAndInvalidTargetRecycleVoices()
    {
        using Fixture f = new();
        Sfx.Play(f.Sounds.ShellWhoosh);
        AudioStreamPlayer player = Assert.IsType<AudioStreamPlayer>(f.Manager.GetChild(0));
        player.EmitSignal(AudioStreamPlayer.SignalName.Finished);
        Assert.Equal(0, f.Manager.ActiveFlatVoices);

        Node2D target = new();
        Sfx.PlayAttached(f.Sounds.ShellWhoosh, target);
        Assert.Equal(1, f.Manager.ActiveSpatialVoices);
        target.Free();
        f.Manager._Process(0);
        Assert.Equal(0, f.Manager.ActiveSpatialVoices);
    }

    [Fact]
    public void FlatPoolGrowsToCapAndStealsOldestEligibleVoice()
    {
        using Fixture f = new();
        SfxHandle oldest = default;
        for (int i = 0; i < Sfx.FLAT_CAP; i++)
        {
            SfxHandle handle = Sfx.Play(f.Sounds.ShellWhoosh);
            if (i == 0) oldest = handle;
        }
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.FlatVoiceCount);
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);

        SfxHandle critical = Sfx.Play(f.Sounds.RegularKill);
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);
        oldest.Stop(); // stale: its voice now belongs to the critical sound
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);
        critical.Stop();
        Assert.Equal(Sfx.FLAT_CAP - 1, f.Manager.ActiveFlatVoices);
    }

    [Fact]
    public void LowerPriorityRequestCannotStealCriticalVoice()
    {
        using Fixture f = new();
        for (int i = 0; i < Sfx.FLAT_CAP; i++)
            Sfx.Play(f.Sounds.RegularKill);

        SfxHandle dropped = Sfx.Play(f.Sounds.ShellWhoosh);
        dropped.Stop();
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.ActiveFlatVoices);
        Assert.Equal(Sfx.FLAT_CAP, f.Manager.FlatVoiceCount);
    }

    [Fact]
    public void SpatialPoolGrowsToItsSimulationSizedCap()
    {
        using Fixture f = new();
        for (int i = 0; i < Sfx.SPATIAL_CAP + 1; i++)
            Sfx.PlayAt(f.Sounds.ShellWhoosh, new Vector2(i, 0));
        Assert.Equal(Sfx.SPATIAL_CAP, f.Manager.SpatialVoiceCount);
        Assert.Equal(Sfx.SPATIAL_CAP, f.Manager.ActiveSpatialVoices);
    }

    [Fact]
    public void MissingStreamAndDefaultHandleAreSafeNoOps()
    {
        using Fixture f = new();
        SoundEffect missing = new();
        Sfx.Play(missing);
        default(SfxHandle).Stop();
        Assert.Equal(0, f.Manager.ActiveFlatVoices);
        missing.Free();
    }

    [Fact]
    public void TimeScaledSoundsFollowClientClockWhileAnnouncementsDoNot()
    {
        using Fixture f = new();

        SfxHandle scaled = Sfx.Play(f.Sounds.ShellWhoosh);
        AudioStreamPlayer scaledPlayer = Assert.IsType<AudioStreamPlayer>(f.Manager.GetChild(0));
        ClientClock.BeginReplay();
        f.Manager._Process(0);
        Assert.Equal(ClientClock.REPLAY_TIME_SCALE, scaledPlayer.PitchScale, precision: 3);
        scaled.Stop();

        SfxHandle announcement = Sfx.Play(f.Sounds.RegularKill);
        AudioStreamPlayer announcementPlayer = Assert.IsType<AudioStreamPlayer>(f.Manager.GetChild(0));
        Assert.Equal(1f, announcementPlayer.PitchScale, precision: 3);
        announcement.Stop();

        ClientClock.Reset();
        f.Manager._Process(0);
    }

    private sealed class Fixture : IDisposable
    {
        public Sfx Manager { get; } = new();
        public SoundRegistry Sounds { get; } = ResourceLoader.Load<SoundRegistry>(
            "res://Assets/Sounds/SoundRegistry.tres")!;

        public Fixture()
        {
            Manager.Set("_sounds", Sounds);
            Manager._Ready();
        }

        public void Dispose()
        {
            ClientClock.Reset();
            Manager._ExitTree();
            Manager.Free();
        }
    }
}
