using System.Collections.Immutable;
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

        PresentedMatchFrame middle = clip.Sample(15);

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

        PresentedMatchFrame middle = clip.Sample(15);

        Assert.Equal(7, middle.Players[0].State.Presentation.KillingSpreeMagnitude);
    }

    [Fact]
    public void CaptureNeedsEnoughHistoryForTheCurrentFallback()
    {
        ReplayHistory oneFrame = new();
        oneFrame.Add(Frame(10, 0));
        Assert.Null(oneFrame.Capture(10));

        ReplayHistory tooShort = new();
        tooShort.Add(Frame(10, 0));
        tooShort.Add(Frame(20, 100));
        Assert.Null(tooShort.Capture(20));
    }

    [Fact]
    public void CaptureRetainsTheExactFramesThatWereRecorded()
    {
        ReplayHistory history = new();
        PresentedMatchFrame first = Frame(0, 0);
        PresentedMatchFrame last = Frame(30, 30);
        history.Add(first);
        history.Add(last);

        ReplayClip clip = Assert.IsType<ReplayClip>(history.Capture(30));

        Assert.Same(first, clip.Sample(clip.StartTick));
        Assert.Same(last, clip.Sample(clip.EndTick));
    }

    private static PresentedMatchFrame Frame(float tick, float x, byte magnitude = 0) => new(
        tick,
        [
            new PresentedPlayer(1, new PlayerViewState(
                new Vector2(x, 0), 0, 0, 5, 0, 100, 0, 0, 0, 0,
                new PlayerPresentationState { KillingSpreeMagnitude = magnitude }))
        ],
        [
            new PresentedMortar(
                PresentedMortarKey.Authoritative(7),
                new Vector2(x, 10),
                new Vec2(1, 2))
        ],
        ImmutableArray<RopeSegment>.Empty);
}
