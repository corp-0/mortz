using System.Runtime.CompilerServices;

namespace Mortz.E2E.Tests.Harness;

internal static class EnvironmentSetup
{
    [ModuleInitializer]
    internal static void LoadEnvironment()
    {
        string path = Path.Combine(RepoRoot.Path, ".env");
        if (File.Exists(path))
            DotNetEnv.Env.NoClobber().Load(path);
    }
}
