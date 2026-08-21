using twodog;
using twodog.Testing;
using Xunit;

namespace Mortz.Tests;

public sealed class MortzGodotFixture : FixtureBase, IDisposable
{
    public MortzGodotFixture() : base(
        "--headless",
        "res://Mortz.Tests/TestRoot.tscn",
        "++",
        "--content-root",
        Path.Combine(Engine.ResolveProjectDir(), "content"))
    {
    }

    public new void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        base.Dispose();
    }

    public void RenderFrame() => GodotInstance.Iteration();
}

[CollectionDefinition(nameof(MortzGodotCollection), DisableParallelization = true)]
public sealed class MortzGodotCollection : ICollectionFixture<MortzGodotFixture>;
