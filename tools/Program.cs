namespace Mortz.Tools;

/// <summary>Dev tools, one subcommand each: dotnet run --project tools -- &lt;tool&gt; ...</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            switch (args.FirstOrDefault())
            {
                case "convert-lxl": ConvertLxl.Run(args[1..]); return 0;
                case "export": Export.Run(args[1..]); return 0;
                case "official": OfficialOverlay.Run(args[1..]); return 0;
                case "gen-sounds": GenSounds.Run(); return 0;
                default:
                    Console.Error.WriteLine("usage:");
                    Console.Error.WriteLine("  dotnet run --project tools -- convert-lxl <path.lxl> <mapId> [--scale N] [--players N] [--out DIR]");
                    Console.Error.WriteLine("  dotnet run --project tools -- export [client|server|all] [--debug] [--require-official]");
                    Console.Error.WriteLine("  dotnet run --project tools -- official check");
                    Console.Error.WriteLine("  dotnet run --project tools -- gen-sounds");
                    return 1;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    /// <summary>Tools resolve paths against the repo root, so refuse to run from
    /// anywhere else. (The old script version once wrote to a drive root.)</summary>
    internal static string RepoRoot()
    {
        if (!File.Exists("project.godot"))
            throw new Exception("run from the repo root (no project.godot in the current directory)");
        return Directory.GetCurrentDirectory();
    }
}
