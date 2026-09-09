using System.Diagnostics;

namespace Mortz.Tools;

public static class PublishDocker
{
    private const string IMAGE = "ghcr.io/corp-0/mortz-server";

    public static string ResolveDocker() => ToolPath.Resolve("DOCKER_PATH", "docker");

    public static void Push(string docker, string root)
    {
        string sha = CaptureGit(root, ["rev-parse", "--short", "HEAD"]).Trim();
        string dockerfile = Path.Combine(root, "tools", "Docker", "Dockerfile");
        foreach (string flavor in new[] { "standalone", "steam" })
        {
            string context = StageContext(root, flavor);
            List<string> args =
            [
                "buildx", "build", "--platform", "linux/amd64", "--push", "--target", flavor,
                "-f", dockerfile, "-t", $"{IMAGE}:{flavor}", "-t", $"{IMAGE}:{flavor}-{sha}"
            ];
            Console.WriteLine($"==> building {IMAGE}:{flavor} and :{flavor}-{sha} from {flavor}/linux/server");
            if (flavor == "standalone")
            {
                // Existing deployments use these tags for the standalone server.
                args.AddRange(["-t", $"{IMAGE}:latest", "-t", $"{IMAGE}:{sha}"]);
                Console.WriteLine($"==> retaining standalone aliases :latest and :{sha}");
            }
            args.Add(context);
            RunDocker(docker, args.ToArray());
        }
    }

    private static string StageContext(string root, string flavor)
    {
        if (flavor is not ("steam" or "standalone"))
        {
            throw new ArgumentException("Docker flavor must be steam or standalone.", nameof(flavor));
        }
        string linuxDirectory = Export.PackageDirectory(root, flavor, "linux", "server");
        List<string> requiredFiles =
        [
            "MortzServer.x86_64", "data_Mortz_linuxbsd_x86_64/Mortz.dll"
        ];
        if (flavor == "steam")
        {
            requiredFiles.AddRange(
            [
                "steam_appid.txt", "libgodotsteam_server.linux.template_release.x86_64.so",
                "libsteam_api.so", "steamclient.so", "libsteamwebrtc.so"
            ]);
        }
        foreach (string file in requiredFiles)
        {
            if (!File.Exists(Path.Combine(linuxDirectory, file)))
            {
                throw new Exception($"{flavor} server package is missing {file}; export server --{flavor} --linux before publishing Docker");
            }
        }
        string context = Path.Combine(root, "build", "docker", flavor);
        if (Directory.Exists(context))
            Directory.Delete(context, recursive: true);
        Directory.CreateDirectory(context);

        CopyTree(linuxDirectory, context);
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
