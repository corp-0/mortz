using Mortz.E2E.Protocol;
using Mortz.E2E.Tests.Harness;
using Xunit;

namespace Mortz.E2E.Tests;

/// <summary>The floor every other scenario stands on. Real processes boot,
/// really connect over ENet, really shut down clean.</summary>
public sealed class SmokeScenarios
{
    [Fact]
    public async Task ServerBootsOnAnOsChosenPortAndShutsDownClean()
    {
        MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(ServerBootsOnAnOsChosenPortAndShutsDownClean),
            Players = [],
        }, TestContext.Current.CancellationToken);

        Assert.True(scenario.GamePort > 0);
        Assert.Equal(-1, scenario.QueryPort);
        PongResponse pong = await scenario.Server.PingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(E2EProtocolSchema.Hash, pong.SchemaHash);
        ServerStateResponse state = await scenario.Server.StateAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EPhase.LOBBY, state.Phase);
        Assert.Empty(state.Players);

        // Throws unless every process answered ShutdownRequest and exited 0.
        await scenario.DisposeAsync();
    }

    [Fact]
    public async Task SetupLeavesEveryClientSittingInTheLobby()
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(SetupLeavesEveryClientSittingInTheLobby),
            Players = ["alice", "bob"],
        }, TestContext.Current.CancellationToken);

        // Setup stops here. Lobby is where a scenario changes server rules
        // before the match starts.
        ServerStateResponse lobby = await scenario.Server.StateAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EPhase.LOBBY, lobby.Phase);
        Assert.Equal(
            ["alice", "bob"],
            lobby.Players.Select(player => player.Name).Order().ToArray());
    }

    [Fact]
    public async Task ReadyAllStartsTheRealMatchOnTheServerAndEveryClient()
    {
        await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(ReadyAllStartsTheRealMatchOnTheServerAndEveryClient),
            Players = ["alice", "bob"],
        }, TestContext.Current.CancellationToken);
        ClientDriver alice = scenario.Client("alice");
        ClientDriver bob = scenario.Client("bob");

        Assert.NotEqual(0, alice.PeerId);
        Assert.NotEqual(alice.PeerId, bob.PeerId);

        await scenario.ReadyAllAsync(TestContext.Current.CancellationToken);

        MatchEnteredEvent entered = await alice.Events.WaitAsync<MatchEnteredEvent>(
            _ => true, scenario.Budget.MatchEvent,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("flat", entered.MapId);

        ServerStateResponse state = await scenario.Server.StateAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EPhase.MATCH, state.Phase);
        // ENet hands out random peer ids, so neither side has a natural order.
        Assert.Equal(
            new[] { alice.PeerId, bob.PeerId }.Order().ToArray(),
            state.Players.Select(player => player.PeerId).Order().ToArray());
    }

    [Fact]
    public async Task DisposeKillsTheServerAndEveryClient()
    {
        MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(DisposeKillsTheServerAndEveryClient),
            Players = ["alice", "bob"],
        }, TestContext.Current.CancellationToken);
        int[] pids = scenario.Clients.Select(client => client.Process.ProcessId)
            .Prepend(scenario.Server.Process.ProcessId)
            .ToArray();
        Assert.Equal(3, pids.Length);

        await scenario.DisposeAsync();

        Assert.All(pids, pid => Assert.True(Exited(pid), $"pid {pid} outlived the scenario"));
    }

    [Fact]
    public async Task AFailedSetupLeavesNothingBehind()
    {
        // The blank name gets rejected after the server and the first client
        // are already up. That's the partial-setup leak we're guarding against.
        string[] liveBefore = LiveRunLocks();

        await Assert.ThrowsAsync<ArgumentException>(() => MortzScenario.StartAsync(
            new ScenarioOptions
            {
                Name = nameof(AFailedSetupLeavesNothingBehind),
                Players = ["alice", "   "],
            },
            TestContext.Current.CancellationToken));

        // The run lock releases last in teardown. No live lock left over means
        // teardown ran all the way through: clients, server, reaper.
        Assert.Equal(liveBefore, LiveRunLocks());
    }

    [Fact]
    public async Task EveryScenarioGetsItsOwnSession()
    {
        MortzScenario first = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(EveryScenarioGetsItsOwnSession),
            Players = ["alice"],
        }, TestContext.Current.CancellationToken);
        string firstRunId = first.RunId;
        int firstPort = first.GamePort;
        string firstArtifacts = first.ArtifactDirectory;
        int firstServerPid = first.Server.Process.ProcessId;
        await first.DisposeAsync();

        await using MortzScenario second = await MortzScenario.StartAsync(new ScenarioOptions
        {
            Name = nameof(EveryScenarioGetsItsOwnSession),
            Players = ["alice"],
        }, TestContext.Current.CancellationToken);

        Assert.NotEqual(firstRunId, second.RunId);
        Assert.NotEqual(firstPort, second.GamePort);
        Assert.NotEqual(firstArtifacts, second.ArtifactDirectory);
        Assert.NotEqual(firstServerPid, second.Server.Process.ProcessId);
        Assert.True(Exited(firstServerPid), "the previous scenario's server is still running");
        // A brand new server means a brand new lobby, with nobody carried over.
        ServerStateResponse state = await second.Server.StateAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(E2EPhase.LOBBY, state.Phase);
        Assert.Single(state.Players);
    }

    private static bool Exited(int processId)
    {
        try
        {
            using System.Diagnostics.Process process =
                System.Diagnostics.Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static string[] LiveRunLocks()
    {
        if (!Directory.Exists(RunLock.Root))
            return [];
        return Directory.GetFiles(RunLock.Root, "*.lock")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(runId => runId != null && RunLock.IsLive(runId))
            .Select(runId => runId!)
            .Order()
            .ToArray();
    }
}
