# Process E2E tests

`Mortz.E2E.Tests` starts real dedicated-server and headless-client processes and drives them over a typed stdin/stdout protocol. Plain `net10.0` xUnit v3 assembly. Godot only runs in child processes. Protocol types are shared with the game through `Mortz.E2E.Protocol`.

## Running

```powershell
dotnet build Mortz.sln
$env:MORTZ_GODOT = "C:\path\to\Godot_v4.7.1-stable_mono_win64_console.exe"
dotnet test Mortz.E2E.Tests
```

Use the `_console` executable on Windows or stdout is empty.

`dotnet test Mortz.Tests` does not need any of this. The E2E suite is run explicitly.

## Scenario shape

Setup and play are two steps:

```csharp
await using MortzScenario scenario = await MortzScenario.StartAsync(new ScenarioOptions
{
    Name = nameof(MyScenario),
    Players = ["alice", "bob"],
});
ClientDriver alice = scenario.Client("alice");
// Everyone is in the lobby here. Change server rules if the scenario needs to.
await scenario.ReadyAllAsync();
// Everyone is in the match here.
```

`StartAsync` brings up the server, launches every client in `Players`, and returns once all of them have seen the full lobby. Change server rules here if you need to, then call `ReadyAllAsync`. It readies everyone and waits for the real phase change and match entry on every client, one call instead of you sequencing three waits.

To make one player shoot another, ask the scenario for the aim instead of computing ballistics in the test:

```csharp
byte aim = await scenario.AimAtAsync(shooter, target);
```

Reads authoritative positions and the live ruleset from the server, brute-forces all 256 aims through the real `MortarSim`. Ignores terrain, assumes a clear arc. Throws instead of returning a bad byte when the target is out of range.

Each scenario owns its own server, clients, run id, OS-assigned port, and artifact directory. No fixture, no shared server. `StartAsync` is the only constructor. The assembly runs scenarios one at a time.

## Watching a run

Set `Windowed = true` on `ScenarioOptions` to see a single scenario, or watch a whole run without touching any test:

```powershell
$env:MORTZ_E2E_WINDOWED = "1"
dotnet test Mortz.E2E.Tests
```

Clients render in tiled 640x360 windows. Server stays headless, it draws nothing. Windowed mode is forced off when a CI signal is present. It doubles every deadline on top of `MORTZ_E2E_TIMEOUT_SCALE`, both multipliers apply together.

## Environment

| Variable | Effect |
|---|---|
| `MORTZ_GODOT` | The Godot Mono executable. Falls back to `GODOT_PATH`, then `PATH`. Required in practice. |
| `MORTZ_E2E_ARTIFACTS` | Artifact root. Defaults to `<repo>/build/e2e/<runId>/<scenario>/`. |
| `MORTZ_E2E_TIMEOUT_SCALE` | Multiplies every named deadline. Use on a slow or loaded machine. |
| `MORTZ_E2E_WINDOWED` | Render every scenario's clients so a human can watch. Ignored in CI. |

## Artifacts

Written on success and failure both, one directory per scenario:

- `<process>.log`: every stdout and stderr line, timestamped.
- `harness.log`: the typed traffic with correlation ids, plus sweep actions.
- `manifest.json`: redacted command lines, pids, the real bound ports, the schema hash, exit codes, corrupt-line and dropped-event counters.

Command lines are redacted before they reach any of these. A secret passed with `--admin-password` never lands in an archived artifact.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Clean shutdown. The only code a scenario asserts. |
| 64 | `E2EExitCode.STDIN_EOF`: the driver was armed and stdin closed, i.e. the testhost died. |

A process started with `--e2e` whose stdin closes before any request logs a loud error and keeps running. Don't want a misconfigured production launch looking like a clean shutdown.

## Teardown

Four layers, in order:

1. Graceful `ShutdownRequest`.
2. The stdin lifeline.
3. A Windows job object that reaps the children when the testhost dies.
4. A stale sweep that kills leftovers of provably dead runs only. A run lock proves liveness, so a concurrent run's server never gets touched.
