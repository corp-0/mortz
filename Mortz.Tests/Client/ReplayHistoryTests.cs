using Godot;
using Mortz.Client.Replay;
using Mortz.Client.Views;
using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Tests.Client;

public class ReplayHistoryTests
{
    [Fact]
    public void CaptureKeepsThreeQuarterSecondEndingAtEvent()
    {
        ReplayHistory history = new();
        for (int tick = 0; tick <= 80; tick++)
        {
            history.Add(Frame(tick, tick));
        }

        ReplayClip clip = Assert.IsType<ReplayClip>(history.Capture(70));

        Assert.Equal(25, clip.StartTick);
        Assert.Equal(70, clip.EndTick);
    }

    [Fact]
    public void SamplingInterpolatesPlayerAndMortarPositions()
    {
        ReplayClip clip = new([
            Frame(10, 0),
            Frame(20, 100),
        ]);

        ReplayFrame middle = clip.Sample(15);

        Assert.Equal(new Vector2(50, 0), middle.Players[0].State.Feet);
        Assert.Equal(new Vector2(50, 10), middle.Mortars[0].Position);
    }

    [Fact]
    public void SamplingStepsTheRecordedPresentationFromTheNewerFrame()
    {
        ReplayClip clip = new([
            Frame(10, 0, magnitude: 5),
            Frame(20, 100, magnitude: 7),
        ]);

        ReplayFrame middle = clip.Sample(15);

        Assert.Equal(7, middle.Players[0].State.Presentation.KillingSpreeMagnitude);
    }

    private static ReplayFrame Frame(float tick, float x, byte magnitude = 0) => new(
        tick,
        [new ReplayPlayer(1, new PlayerViewState(
            new Vector2(x, 0), 0, 0, 5, 0, 100, 0, 0, 0, 0,
            new PlayerPresentationState(magnitude)))],
        [new ReplayMortar(7, new Vector2(x, 10), new Vec2(1, 2))],
        []);
}
