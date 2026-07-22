using twodog.fixture;
using Xunit;

namespace Mortz.Tests;

public sealed class MortzGodotFixture()
    : GodotFixtureBase(
        "--headless",
        "res://Mortz.Tests/TestRoot.tscn",
        "++",
        "--content-root",
        Path.Combine(twodog.Engine.ResolveProjectDir(), "content"));

[CollectionDefinition(nameof(MortzGodotCollection), DisableParallelization = true)]
public sealed class MortzGodotCollection : ICollectionFixture<MortzGodotFixture>;
