using Xunit;

namespace Mortz.Tests.Core.Net;

public class ClientTransportArchitectureTests
{
    [Fact]
    public void ProductionDoesNotReferenceTheRemovedGlobalTransport()
    {
        string root = FindRepositoryRoot();
        string forbidden = "Net" + "Transport";
        string[] sourceRoots = ["Mortz.Core", "Mortz.Server", "Mortz.Net.Gen", "src"];

        foreach (string sourceRoot in sourceRoots)
        {
            foreach (string file in Directory.EnumerateFiles(
                         Path.Combine(root, sourceRoot), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                Assert.DoesNotContain(forbidden, File.ReadAllText(file),
                    StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Mortz.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ??
               throw new DirectoryNotFoundException("Could not find the Mortz repository root.");
    }
}
