using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// One scenario: a real dedicated server, real headless clients, the real
/// lobby. StartAsync ends with every client joined and confirmed in the lobby,
/// which is where a scenario changes server rules; ReadyAllAsync is the
/// separate, explicit step that starts the match.
/// </summary>
public sealed class MortzScenario : IAsyncDisposable
{
    private static readonly SemaphoreSlim _sweepOnce = new(1, 1);
    private static bool _swept;

    /// <summary>A watched run competes with a compositor and a human's machine,
    /// so every deadline gets more room. Multiplies MORTZ_E2E_TIMEOUT_SCALE,
    /// never replaces it.</summary>
    private const double WINDOWED_SLACK = 2;

    private readonly List<ClientDriver> _clients = [];
    private readonly ScenarioOptions _options;
    private readonly ScenarioArtifacts _artifacts;
    private readonly ProcessReaper _reaper;
    private readonly RunLock _runLock;
    private readonly string _godot;
    private readonly IReadOnlyList<string> _sweepActions;
    private readonly bool _windowed;
    private readonly E2EProcess _serverProcess;
    private MatchSetup? _setup;
    private int _gamePort;
    private int _queryPort = -1;
    private bool _torndown;

    public ServerDriver Server { get; }

    public E2EBudget Budget { get; }

    public IReadOnlyList<ClientDriver> Clients => _clients;

    /// <summary>Unique per scenario; names the artifact directory, the run lock
    /// and the --e2e-run marker every child carries.</summary>
    public string RunId { get; }

    /// <summary>The port ENet actually bound, since the server starts with
    /// --port 0.</summary>
    public int GamePort => _gamePort;

    /// <summary>-1 unless the scenario asked for a query port.</summary>
    public int QueryPort => _queryPort;

    public string ArtifactDirectory => _artifacts.Directory;

    private MortzScenario(
        ScenarioOptions options,
        string runId,
        string godot,
        ScenarioArtifacts artifacts,
        ProcessReaper reaper,
        RunLock runLock,
        IReadOnlyList<string> sweepActions,
        E2EProcess serverProcess,
        E2EBudget budget,
        bool windowed)
    {
        _options = options;
        RunId = runId;
        _godot = godot;
        _artifacts = artifacts;
        _reaper = reaper;
        _runLock = runLock;
        _sweepActions = sweepActions;
        _serverProcess = serverProcess;
        _windowed = windowed;
        Budget = budget;
        Server = new ServerDriver(serverProcess, budget);
    }

    /// <summary>The client that joined under this name.</summary>
    public ClientDriver Client(string name) =>
        _clients.SingleOrDefault(client => client.Name == name)
        ?? throw new E2EProcessException(
            $"No client named '{name}'; the scenario has " +
            $"[{string.Join(", ", _clients.Select(client => client.Name))}].");

    /// <summary>Server up, every client joined, everyone sitting in the lobby.</summary>
    public static async Task<MortzScenario> StartAsync(
        ScenarioOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Players);
        if (options.Mode != null && options.RulesetPath != null)
            throw new ArgumentException("Mode and RulesetPath are mutually exclusive.", nameof(options));

        string runId = Guid.NewGuid().ToString("N")[..12];
        string godot = GodotExecutable.Resolve();
        // Nobody is watching an unattended run, so it never pays for windows.
        bool wanted = options.Windowed || WatchRequested();
        bool windowed = wanted && !CiEnvironment.Detected;
        E2EBudget budget = new(options.Timescale,
            E2EBudget.EnvironmentScale * (windowed ? WINDOWED_SLACK : 1));
        RunLock runLock = RunLock.Acquire(runId);
        ScenarioArtifacts artifacts = ScenarioArtifacts.Create(runId, options.Name);
        IReadOnlyList<string> sweepActions = await SweepOnceAsync(runId, godot);
        foreach (string action in sweepActions)
        {
            artifacts.Harness($"[sweep] {action}");
        }
        ProcessReaper reaper = ProcessReaper.Create();

        E2EProcess server;
        try
        {
            server = E2EProcess.Start(new E2EProcessStart(
                "server", godot, RepoRoot.Path, ServerArguments(runId, options), artifacts, reaper));
        }
        catch
        {
            // Nothing launched, so there is nothing to reap: just let go of the
            // run's own resources.
            reaper.Dispose();
            artifacts.Dispose();
            runLock.Dispose();
            throw;
        }

        if (wanted && !windowed)
            artifacts.Harness("[harness] windowed mode requested but ignored: this looks like CI");
        MortzScenario scenario = new(options, runId, godot, artifacts, reaper, runLock,
            sweepActions, server, budget, windowed);
        try
        {
            await scenario.BindAsync(cancellationToken);
            await scenario.JoinLobbyAsync(cancellationToken);
            return scenario;
        }
        catch
        {
            // Everything this scenario started goes down, clients that already
            // joined included, before the original failure surfaces.
            await scenario.AbandonAsync();
            throw;
        }
    }

    private async Task BindAsync(CancellationToken cancellationToken)
    {
        await _serverProcess.WaitUntilReadyAsync(
            E2EProcessRole.SERVER, Budget.Ready, cancellationToken);
        ServerListeningEvent listening = await _serverProcess.Events
            .WaitAsync<ServerListeningEvent>(
                _ => true, Budget.Ready, cancellationToken: cancellationToken);
        if (listening.GamePort <= 0)
            throw _serverProcess.Failure($"The server reported game port {listening.GamePort}.");
        _gamePort = listening.GamePort;
        _queryPort = listening.QueryPort;
    }

    /// <summary>Launches the cast and waits until every one of them has seen the
    /// full lobby. Setup ends here: rules are changed in the lobby, so starting
    /// the match stays the scenario's decision.</summary>
    private async Task JoinLobbyAsync(CancellationToken cancellationToken)
    {
        foreach (string name in _options.Players)
        {
            await AddClientAsync(name, cancellationToken);
        }
        foreach (ClientDriver client in _clients)
        {
            await client.Events.WaitAsync<LobbyRosterObservedEvent>(
                value => value.PlayerCount >= _clients.Count,
                Budget.Connect,
                cancellationToken: cancellationToken);
        }
    }

    private async Task AddClientAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        E2EProcess client = E2EProcess.Start(new E2EProcessStart(
            name, _godot, RepoRoot.Path, ClientArguments(name, _clients.Count),
            _artifacts, _reaper));
        ClientDriver driver;
        try
        {
            await client.WaitUntilReadyAsync(E2EProcessRole.CLIENT, Budget.Ready, cancellationToken);

            Task<ConnectedEvent> connected = client.Events.WaitAsync<ConnectedEvent>(
                _ => true, Budget.Connect, cancellationToken: cancellationToken);
            Task<ConnectionFailedEvent> refused = client.Events.WaitAsync<ConnectionFailedEvent>(
                _ => true, Budget.Connect, cancellationToken: cancellationToken);
            Task first = await Task.WhenAny(connected, refused);
            if (first == refused && refused.IsCompletedSuccessfully)
                throw client.Failure($"Client '{name}' could not reach 127.0.0.1:{_gamePort}.");

            ConnectedEvent joined = await connected;
            driver = new ClientDriver(client, Budget, name, joined.PeerId);
        }
        catch
        {
            // Not in _clients yet, so scenario teardown would never see it.
            await QuietlyAsync(client);
            throw;
        }
        _clients.Add(driver);
    }

    /// <summary>Readies everyone and waits for the real phase change and match
    /// entry on every client, so a test makes one call rather than sequencing
    /// three waits.</summary>
    public async Task ReadyAllAsync(CancellationToken cancellationToken = default)
    {
        if (_clients.Count == 0)
            throw new InvalidOperationException("A match needs at least one client.");

        E2EEventCursor beforeReady = Server.Events.Cursor;
        foreach (ClientDriver client in _clients)
        {
            await client.SetReadyAsync(true, cancellationToken);
        }

        await Server.Events.WaitAsync<PhaseChangedEvent>(
            value => value.Phase == E2EPhase.MATCH,
            Budget.MatchEvent,
            beforeReady,
            cancellationToken);
        foreach (ClientDriver client in _clients)
        {
            await client.Events.WaitAsync<MatchEnteredEvent>(
                _ => true, Budget.MatchEvent, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// The aim byte that kills the victim from where the server says both
    /// players actually are. Reads authoritative positions and the live
    /// ruleset, then brute-forces the answer through the real MortarSim, so it
    /// stays right when the physics changes.
    ///
    /// Terrain is ignored: the arc is assumed clear, which holds on the flat
    /// map. An unreachable target throws rather than returning a bad byte,
    /// because that is a bug in the scenario.
    /// </summary>
    public async Task<byte> AimAtAsync(
        ClientDriver shooter, ClientDriver victim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        ArgumentNullException.ThrowIfNull(victim);
        ServerStateResponse state = await Server.StateAsync(cancellationToken);
        MatchSetup setup = await SetupAsync(cancellationToken);
        return MortarAimSolver.Solve(new MortarAimQuery(
            Aiming(state, shooter.Name, shooter.PeerId),
            Aiming(state, victim.Name, victim.PeerId),
            setup.Config.Combat,
            setup.TerrainWidth,
            setup.TerrainHeight)).Aim;
    }

    /// <summary>Rules and arena, fetched once: they cannot change inside a match.</summary>
    private async Task<MatchSetup> SetupAsync(CancellationToken cancellationToken)
    {
        if (_setup is MatchSetup cached)
            return cached;
        MatchSetupResponse response = await Server.SetupAsync(cancellationToken);
        MatchSetup setup = new(
            MatchConfig.FromBytes(response.Config),
            response.TerrainWidth,
            response.TerrainHeight);
        _setup = setup;
        return setup;
    }

    /// <summary>The solver reads a PlayerState, so the server's view of one
    /// player is rebuilt into the shape the sim uses.</summary>
    private static PlayerState Aiming(ServerStateResponse state, string name, int peerId)
    {
        E2EServerPlayer player = state.Players.SingleOrDefault(value => value.PeerId == peerId);
        if (player.PeerId != peerId)
            throw new E2EProcessException($"The server does not have '{name}' in the match.");
        return new PlayerState
        {
            PeerId = player.PeerId,
            Position = player.Position,
            Velocity = player.Velocity,
            Health = (byte)player.Health,
        };
    }

    public ValueTask DisposeAsync() => TeardownAsync(strict: true);

    /// <summary>Teardown for a setup that never finished. The same path as
    /// dispose, except a teardown complaint must not replace the failure that
    /// caused it.</summary>
    private async Task AbandonAsync()
    {
        try
        {
            await TeardownAsync(strict: false);
        }
        catch (Exception exception)
        {
            _artifacts.Harness($"[harness] teardown after a failed setup threw: {exception}");
        }
    }

    private async ValueTask TeardownAsync(bool strict)
    {
        if (_torndown)
            return;
        _torndown = true;

        List<string> failures = [];
        foreach (ClientDriver client in _clients)
        {
            await ShutdownAsync(client.Process, () => client.ShutdownAsync(), failures);
        }
        await ShutdownAsync(_serverProcess, () => Server.ShutdownAsync(), failures);

        _artifacts.WriteManifest(new E2EManifest(
            RunId,
            _options.Name,
            E2EProtocolSchema.Hash,
            _options.Timescale,
            _gamePort,
            _queryPort,
            _sweepActions,
            _clients.Select(client => client.Process.Describe())
                .Prepend(_serverProcess.Describe())
                .ToArray()));

        foreach (string failure in failures)
        {
            _artifacts.Harness($"[harness] {failure}");
        }
        // The reaper handle closes after every child is gone, the run lock last:
        // until it releases, another run must treat this one as alive.
        _reaper.Dispose();
        _artifacts.Dispose();
        _runLock.Dispose();

        if (strict && failures.Count > 0)
            throw new E2EProcessException(string.Join(Environment.NewLine, failures));
    }

    private async Task ShutdownAsync(
        E2EProcess process, Func<Task> request, List<string> failures)
    {
        try
        {
            await request();
        }
        catch (Exception exception) when (exception is E2EProcessException or TimeoutException)
        {
            failures.Add($"[{process.Name}] shutdown request failed: {exception.Message}");
        }

        await process.StopAsync(Budget.Shutdown);
        int? exitCode = process.ExitCode;
        if (exitCode != E2EExitCode.CLEAN)
            failures.Add($"[{process.Name}] exited with {exitCode}, expected 0.{Environment.NewLine}{process.Report()}");
        await process.DisposeAsync();
    }

    /// <summary>Stop a process the scenario never took ownership of.</summary>
    private async Task QuietlyAsync(E2EProcess process)
    {
        try
        {
            await process.StopAsync(Budget.Shutdown);
            await process.DisposeAsync();
        }
        catch (Exception exception)
        {
            _artifacts.Harness($"[harness] could not stop '{process.Name}': {exception}");
        }
    }

    private static IReadOnlyList<string> ServerArguments(string runId, ScenarioOptions options)
    {
        List<string> arguments =
        [
            "--path", RepoRoot.Path, "--headless", "++",
            "--server", "--e2e", "--e2e-run", runId,
            "--port", "0",
            "--map", options.MapId,
            "--timescale", Timescale(options),
        ];
        if (options.RulesetPath != null)
            arguments.AddRange(["--ruleset", options.RulesetPath]);
        else if (options.Mode != null)
            arguments.AddRange(["--mode", options.Mode]);
        if (options.QueryPort is int queryPort)
            arguments.AddRange(["--query-port", queryPort.ToString()]);
        return arguments;
    }

    private IReadOnlyList<string> ClientArguments(string name, int index)
    {
        List<string> arguments = ["--path", RepoRoot.Path];
        arguments.AddRange(WindowArguments(index));
        arguments.AddRange([
            "++",
            "--e2e", "--e2e-run", RunId,
            "--connect", "127.0.0.1",
            "--port", _gamePort.ToString(),
            "--name", name,
            "--timescale", Timescale(_options),
        ]);
        if (name == _options.SlowScreenPlayer)
        {
            arguments.AddRange([
                "--e2e-screen-load-delay-ms", _options.ScreenLoadDelayMs.ToString(),
            ]);
        }
        return arguments;
    }

    /// <summary>--headless is the only thing hiding the window, so a watched
    /// client just drops it and says where to sit.</summary>
    private IReadOnlyList<string> WindowArguments(int index)
    {
        if (!_windowed)
            return ["--headless"];
        WindowPlacement placement = WindowLayout.For(index);
        return
        [
            "--resolution", $"{placement.Width}x{placement.Height}",
            "--position", $"{placement.X},{placement.Y}",
        ];
    }

    /// <summary>MORTZ_E2E_WINDOWED watches a whole run without editing a test.</summary>
    private static bool WatchRequested()
    {
        string? value = Environment.GetEnvironmentVariable("MORTZ_E2E_WINDOWED");
        return !string.IsNullOrWhiteSpace(value) &&
               !value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0";
    }

    private static string Timescale(ScenarioOptions options) =>
        options.Timescale.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<IReadOnlyList<string>> SweepOnceAsync(string runId, string godot)
    {
        await _sweepOnce.WaitAsync();
        try
        {
            if (_swept)
                return [];
            _swept = true;
            return StaleProcessSweep.Run(runId, godot);
        }
        finally
        {
            _sweepOnce.Release();
        }
    }
}
