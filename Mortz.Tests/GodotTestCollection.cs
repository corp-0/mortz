using twodog.Testing;
using Xunit;

namespace Mortz.Tests;

public sealed class MortzGodotFixture()
    : FixtureBase(
        "--headless",
        "res://Mortz.Tests/TestRoot.tscn",
        "++",
        "--content-root",
        Path.Combine(twodog.Engine.ResolveProjectDir(), "content")), IDisposable
{
    public new void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        base.Dispose();
    }
}

[CollectionDefinition(nameof(MortzGodotCollection), DisableParallelization = true)]
public sealed class MortzGodotCollection : ICollectionFixture<MortzGodotFixture>;
