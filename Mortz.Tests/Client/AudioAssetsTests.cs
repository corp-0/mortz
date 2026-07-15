using Godot;
using Mortz.Client;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(GodotHeadlessCollection))]
public class AudioAssetsTests
{
    [Fact]
    public void AudioResourcesAndScenesLoad()
    {
        AudioBusLayout? buses = ResourceLoader.Load<AudioBusLayout>("res://default_bus_layout.tres");
        SoundRegistry? sounds = ResourceLoader.Load<SoundRegistry>(
            "res://Assets/Sounds/SoundRegistry.tres");
        PackedScene? clientScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/ClientMain.tscn");
        PackedScene? gameScene = ResourceLoader.Load<PackedScene>(
            "res://src/Shared/Scenes/GameView.tscn");

        Assert.NotNull(buses);
        Assert.NotNull(sounds);
        Assert.All(sounds.Entries(), entry => Assert.NotNull(entry.Sound.Stream));
        Assert.All(sounds.Entries(), entry =>
            Assert.True(AudioServer.GetBusIndex(entry.Sound.BusName) >= 0,
                $"{entry.Name} maps to missing bus '{entry.Sound.BusName}'"));
        Assert.Equal(SoundBus.SFX, sounds.MortarReload.Bus);
        Assert.Equal(SoundBus.UI, sounds.FirstBlood.Bus);
        Assert.Equal(SoundBus.UI, sounds.Owned.Bus);
        AudioStreamWav whoosh = Assert.IsType<AudioStreamWav>(sounds.ShellWhoosh.Stream);
        Assert.NotEqual(AudioStreamWav.LoopModeEnum.Disabled, whoosh.LoopMode);

        Node client = Assert.IsAssignableFrom<Node>(clientScene!.Instantiate());
        Assert.IsType<Sfx>(client.GetNode("Sfx"));
        client.Free();

        Node game = Assert.IsAssignableFrom<Node2D>(gameScene!.Instantiate());
        Assert.IsType<KillAnnouncer>(game.GetNode("KillAnnouncer"));
        game.Free();
    }
}
