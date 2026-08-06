using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// Kills leftovers of runs that are provably dead, and nothing else. A
/// candidate needs our marker, a different run id, the resolved Godot binary,
/// and a run lock nobody holds. A concurrent run's live server fails the last
/// test, so it is left alone.
/// </summary>
public static class StaleProcessSweep
{
    public const string MARKER = "--e2e-run";

    public static IReadOnlyList<string> Run(string currentRunId, string godotExecutable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRunId);
        if (!OperatingSystem.IsWindows())
            return ["sweep skipped: not Windows"];
        return SweepWindows(currentRunId, godotExecutable);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> SweepWindows(string currentRunId, string godotExecutable)
    {
        List<string> actions = [];
        string godot = Path.GetFullPath(godotExecutable);
        using ManagementObjectSearcher searcher = new(
            "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process " +
            $"WHERE CommandLine LIKE '%{MARKER}%'");
        foreach (ManagementBaseObject row in searcher.Get())
        {
            using ManagementBaseObject candidate = row;
            string? commandLine = candidate["CommandLine"] as string;
            string? executable = candidate["ExecutablePath"] as string;
            if (commandLine == null || executable == null)
                continue;
            if (!string.Equals(Path.GetFullPath(executable), godot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (RunIdOf(commandLine) is not string runId || runId == currentRunId)
                continue;
            if (RunLock.IsLive(runId))
            {
                actions.Add($"left run {runId} alone: its lock is held");
                continue;
            }
            actions.Add(Kill(Convert.ToInt32(candidate["ProcessId"]), runId));
            RunLock.Forget(runId);
        }
        return actions;
    }

    private static string Kill(int processId, string runId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            return $"killed stale pid {processId} from dead run {runId}";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return $"could not kill stale pid {processId} from dead run {runId}: {exception.Message}";
        }
    }

    /// <summary>The token after the marker, or null when the command line does
    /// not actually carry one.</summary>
    public static string? RunIdOf(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        string[] tokens = commandLine.Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Trim('"') == MARKER)
                return tokens[i + 1].Trim('"');
        }
        return null;
    }
}
