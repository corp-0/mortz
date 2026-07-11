using twodog.xunit;
using Xunit;

namespace Mortz.Tests.Setup;

[CollectionDefinition("Godot")]
public class GodotCollection : ICollectionFixture<GodotHeadlessFixture>;
