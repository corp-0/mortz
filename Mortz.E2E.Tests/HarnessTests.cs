using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

public sealed class HarnessTests
{
    [Fact]
    public async Task EventPublishedBeforeWaitIsObserved()
    {
        E2EEventStream events = new();
        ProcessReadyEvent expected = new(E2EProcessRole.SERVER, 8, E2EProtocolSchema.Hash);
        events.Publish(expected);

        ProcessReadyEvent actual = await events.WaitAsync<ProcessReadyEvent>(
            value => value.Role == E2EProcessRole.SERVER,
            TimeSpan.FromSeconds(1),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ACursorHidesEverythingPublishedBeforeIt()
    {
        E2EEventStream events = new();
        events.Publish(new MatchTickEvent(60));
        E2EEventCursor after = events.Cursor;

        Task<MatchTickEvent> waiting = events.WaitAsync<MatchTickEvent>(
            _ => true,
            TimeSpan.FromSeconds(5),
            after,
            TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);

        events.Publish(new MatchTickEvent(120));

        Assert.Equal(120, (await waiting).Tick);
    }

    [Fact]
    public async Task ADefaultCursorStillScansTheWholeHistory()
    {
        E2EEventStream events = new();
        events.Publish(new MatchTickEvent(60));

        MatchTickEvent observed = await events.WaitAsync<MatchTickEvent>(
            _ => true,
            TimeSpan.FromSeconds(1),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(60, observed.Tick);
    }

    [Fact]
    public async Task TheCursorAdvancesWithEveryEvent()
    {
        E2EEventStream events = new();
        E2EEventCursor empty = events.Cursor;
        events.Publish(new MatchTickEvent(60));
        E2EEventCursor one = events.Cursor;
        events.Publish(new MatchTickEvent(120));

        Assert.Equal(0, empty.Sequence);
        Assert.Equal(1, one.Sequence);
        Assert.Equal(2, events.Cursor.Sequence);
        Assert.Equal(0, events.DroppedEventCount);
        MatchTickEvent latest = await events.WaitAsync<MatchTickEvent>(
            _ => true, TimeSpan.FromSeconds(1), one, TestContext.Current.CancellationToken);
        Assert.Equal(120, latest.Tick);
    }

    [Fact]
    public async Task AnOverrunHistoryDropsEventsAndRefusesAStaleCursor()
    {
        E2EEventStream events = new();
        E2EEventCursor stale = events.Cursor;
        for (int tick = 0; tick < 4200; tick++)
        {
            events.Publish(new MatchTickEvent(tick));
        }

        Assert.Equal(104, events.DroppedEventCount);
        E2EProcessException exception = await Assert.ThrowsAsync<E2EProcessException>(
            () => events.WaitAsync<PhaseChangedEvent>(
                _ => true, TimeSpan.FromSeconds(1), stale, TestContext.Current.CancellationToken));
        Assert.Contains("104", exception.Message);
    }

    [Fact]
    public async Task EventStreamFailureFailsWaiters()
    {
        E2EEventStream events = new();
        InvalidOperationException failure = new("broken");
        events.Fail(failure);

        E2EProcessException exception = await Assert.ThrowsAsync<E2EProcessException>(
            () => events.WaitAsync<ProcessReadyEvent>(
                _ => true,
                TimeSpan.FromSeconds(1),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public void EveryNamedDeadlineScalesLinearly()
    {
        E2EBudget plain = new(timescale: 1, scale: 1);
        E2EBudget stretched = new(timescale: 1, scale: 3);

        Assert.Equal(plain.Ready * 3, stretched.Ready);
        Assert.Equal(plain.Connect * 3, stretched.Connect);
        Assert.Equal(plain.Response * 3, stretched.Response);
        Assert.Equal(plain.MatchEvent * 3, stretched.MatchEvent);
        Assert.Equal(plain.Shutdown * 3, stretched.Shutdown);
    }

    [Fact]
    public void SimTicksShrinkWithTimescaleButNeverBelowTheFloor()
    {
        E2EBudget slow = new(timescale: 1, scale: 1);
        E2EBudget fast = new(timescale: 4, scale: 1);
        int ticks = SimConfig.TICK_RATE * 10;

        Assert.True(fast.SimTicks(ticks) < slow.SimTicks(ticks));
        Assert.True(fast.SimTicks(1) >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SimTicksAllowsMoreThanTheIdealWallTime()
    {
        E2EBudget budget = new(timescale: 1, scale: 1);
        int ticks = SimConfig.TICK_RATE * 10;

        Assert.True(budget.SimTicks(ticks) > TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ANonPositiveScaleIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new E2EBudget(timescale: 1, scale: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new E2EBudget(timescale: 0, scale: 1));
    }

    [Fact]
    public void WindowedSlackMultipliesTheEnvironmentScaleRatherThanReplacingIt()
    {
        E2EBudget plain = new(timescale: 1, scale: 1);
        E2EBudget stretchedAndWatched = new(timescale: 1, scale: 3 * 2);

        // A watched run on a slow machine must get both allowances, not one.
        Assert.Equal(plain.MatchEvent * 6, stretchedAndWatched.MatchEvent);
    }

    [Theory]
    [InlineData("CI", "true")]
    [InlineData("CI", "1")]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("TF_BUILD", "True")]
    public void CiIsDetectedFromTheUsualSignals(string name, string value) =>
        Assert.True(CiEnvironment.DetectedIn(key => key == name ? value : null));

    [Theory]
    [InlineData("CI", "false")]
    [InlineData("CI", "0")]
    [InlineData("CI", "")]
    [InlineData("SOMETHING_ELSE", "true")]
    public void ADeveloperMachineIsNotMistakenForCi(string name, string value) =>
        Assert.False(CiEnvironment.DetectedIn(key => key == name ? value : null));

    [Fact]
    public void TiledWindowsNeverOverlap()
    {
        WindowPlacement[] placements = Enumerable.Range(0, 6)
            .Select(WindowLayout.For)
            .ToArray();

        for (int a = 0; a < placements.Length; a++)
        {
            for (int b = a + 1; b < placements.Length; b++)
            {
                Assert.False(Overlaps(placements[a], placements[b]),
                    $"window {a} at {placements[a]} overlaps window {b} at {placements[b]}");
            }
        }
    }

    [Fact]
    public void TiledWindowsStayOnScreenAndSideBySide()
    {
        WindowPlacement first = WindowLayout.For(0);
        WindowPlacement second = WindowLayout.For(1);

        Assert.True(first.X >= 0 && first.Y >= 0);
        Assert.Equal(first.Y, second.Y);
        Assert.True(second.X > first.X);
        Assert.Equal(WindowLayout.WIDTH, first.Width);
        Assert.Equal(WindowLayout.HEIGHT, first.Height);
    }

    private static bool Overlaps(WindowPlacement a, WindowPlacement b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width &&
        a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    [Fact]
    public void TheAdminPasswordNeverReachesAnArtifact()
    {
        IReadOnlyList<string> redacted = CommandLineRedaction.Redact(
            ["--server", "--admin-password", "correct horse", "--port", "0"]);

        Assert.Equal(
            ["--server", "--admin-password", CommandLineRedaction.PLACEHOLDER, "--port", "0"],
            redacted);
    }

    [Fact]
    public void RedactionLeavesOrdinaryFlagsAlone()
    {
        IReadOnlyList<string> redacted = CommandLineRedaction.Redact(["--map", "castlewars"]);

        Assert.Equal(["--map", "castlewars"], redacted);
    }

    [Fact]
    public void ATrailingSecretFlagWithNoValueIsHarmless()
    {
        IReadOnlyList<string> redacted = CommandLineRedaction.Redact(["--admin-password"]);

        Assert.Equal(["--admin-password"], redacted);
    }

    [Fact]
    public void TheFormattedCommandLineIsRedactedToo()
    {
        string line = CommandLineRedaction.Format(
            "godot.exe", ["--admin-password", "hunter2"]);

        Assert.DoesNotContain("hunter2", line);
        Assert.Contains(CommandLineRedaction.PLACEHOLDER, line);
    }

    [Fact]
    public void AHeldRunLockReadsAsLive()
    {
        string runId = $"test-{Guid.NewGuid():N}";

        using (RunLock held = RunLock.Acquire(runId))
        {
            Assert.Equal(runId, held.RunId);
            Assert.True(RunLock.IsLive(runId));
        }

        Assert.False(RunLock.IsLive(runId));
        Assert.False(File.Exists(RunLock.PathFor(runId)));
    }

    [Fact]
    public void AnAbandonedLockFileReadsAsDead()
    {
        string runId = $"test-{Guid.NewGuid():N}";
        Directory.CreateDirectory(RunLock.Root);
        File.WriteAllText(RunLock.PathFor(runId), "");
        try
        {
            Assert.False(RunLock.IsLive(runId));
        }
        finally
        {
            RunLock.Forget(runId);
        }
    }

    [Fact]
    public void AnUnknownRunIsNotLive() =>
        Assert.False(RunLock.IsLive($"never-{Guid.NewGuid():N}"));

    [Fact]
    public void TheSweepReadsTheRunIdOutOfACommandLine()
    {
        Assert.Equal("abc123", StaleProcessSweep.RunIdOf(
            "\"C:\\godot.exe\" --headless ++ --server --e2e --e2e-run abc123 --port 0"));
        Assert.Null(StaleProcessSweep.RunIdOf("godot.exe --headless ++ --server"));
        Assert.Null(StaleProcessSweep.RunIdOf("godot.exe --e2e-run"));
    }

    [Fact]
    public void ProcessLogKeepsABoundedTail()
    {
        ProcessLog log = new(capacity: 2);
        log.Add("stdout", "first");
        log.Add("stderr", "second");
        log.Add("stdout", "third");

        string tail = log.Tail();

        Assert.DoesNotContain("first", tail);
        Assert.Contains("[stderr] second", tail);
        Assert.Contains("[stdout] third", tail);
    }

    [Fact]
    public void EveryLineReachesTheSinkEvenAfterTheTailDropsIt()
    {
        List<ProcessLogLine> sunk = [];
        ProcessLog log = new(sunk.Add, capacity: 1);

        log.Add("stdout", "first");
        log.Add("stdout", "second");

        Assert.Equal(["first", "second"], sunk.Select(line => line.Text).ToArray());
        Assert.DoesNotContain("first", log.Tail());
    }

    [Fact]
    public void ArtifactsCollectProcessAndHarnessLinesAndAManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mortz-e2e-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("MORTZ_E2E_ARTIFACTS", root);
        try
        {
            using (ScenarioArtifacts artifacts = ScenarioArtifacts.Create("run1", "Scenario"))
            {
                artifacts.AppendProcess("server", new ProcessLogLine(
                    DateTimeOffset.UtcNow, "stdout", "listening"));
                artifacts.Harness("started");
                artifacts.WriteManifest(new E2EManifest("run1", "Scenario", "hash", 4, 51000, -1,
                    ["swept nothing"],
                    [new E2EProcessRecord("server", 7, "godot --e2e", 0, 2, 3)]));
            }

            string directory = Path.Combine(root, "run1", "Scenario");
            Assert.Contains("listening", File.ReadAllText(Path.Combine(directory, "server.log")));
            Assert.Contains("started", File.ReadAllText(Path.Combine(directory, "harness.log")));
            string manifest = File.ReadAllText(Path.Combine(directory, "manifest.json"));
            Assert.Contains("\"CorruptLines\": 2", manifest);
            Assert.Contains("\"DroppedEvents\": 3", manifest);
            Assert.Contains("swept nothing", manifest);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MORTZ_E2E_ARTIFACTS", null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitMortzGodotPathWins()
    {
        string configured = Path.GetFullPath(Path.Combine("tools", "godot-test"));

        string resolved = GodotExecutable.Resolve(
            configured,
            Path.Combine("ignored", "godot"),
            null,
            candidate => candidate == configured);

        Assert.Equal(configured, resolved);
    }

    [Fact]
    public void InvalidExplicitPathDoesNotSilentlyFallBack()
    {
        string configured = Path.GetFullPath(Path.Combine("missing", "godot"));

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
            GodotExecutable.Resolve(configured, null, null, _ => false));

        Assert.Equal(configured, exception.FileName);
    }
}
