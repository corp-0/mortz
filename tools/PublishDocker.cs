using System.Diagnostics;

namespace Mortz.Tools;

public static class PublishDocker
{
    private const string IMAGE = "ghcr.io/corp-0/mortz-server";

    public static string ResolveDocker() => ToolPath.Resolve("DOCKER_PATH", "docker");

    public static void Push(string docker, string root)
    {
        string sha = CaptureGit(root, ["rev-parse", "--short", "HEAD"]).Trim();
        string context = StageContext(root);
        string dockerfile = Path.Combine(root, "tools", "Docker", "Dockerfile");

        Console.WriteLine($"==> building {IMAGE}:latest and :{sha}");
        RunDocker(docker, ["buildx", "build", "--platform", "linux/amd64", "--push",
            "-f", dockerfile, "-t", $"{IMAGE}:latest", "-t", $"{IMAGE}:{sha}", context]);
    }

    private static string StageContext(string root)
    {
        string linuxDirectory = Path.Combine(root, "build", "Mortz-lin");
        string context = Path.Combine(root, "build", "docker");
        if (Directory.Exists(context))
            Directory.Delete(context, recursive: true);
        Directory.CreateDirectory(context);

        File.Copy(Path.Combine(linuxDirectory, "MortzServer.x86_64"),
            Path.Combine(context, "MortzServer.x86_64"));
        CopyTree(Path.Combine(linuxDirectory, "content"), Path.Combine(context, "content"));
        // The pck does not carry the .NET runtime and assemblies.
        CopyTree(Path.Combine(linuxDirectory, "data_Mortz_linuxbsd_x86_64"),
            Path.Combine(context, "data_Mortz_linuxbsd_x86_64"));
        return context;
    }

    private static void CopyTree(string source, string dest)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string CaptureGit(string root, string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            WorkingDirectory = root
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }
        using Process proc = Process.Start(psi)!;
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new Exception($"git {string.Join(' ', args)} failed (exit {proc.ExitCode})");
        return output;
    }

    private static void RunDocker(string docker, string[] args)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(docker) { UseShellExecute = false };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new Exception($"docker {args[0]} failed (exit {process.ExitCode}); " +
                                "if the push was denied, run 'docker login ghcr.io' once with a write:packages PAT");
        }
    }
}
