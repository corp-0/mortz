using Godot;
using twodog.xunit;
using Xunit;

namespace Mortz.Tests;

[Collection(nameof(GodotHeadlessCollection))]
public class SmokeTests
{
    [Fact]
    public void NodeCanBeCreatedAndNamed()
    {
        Node node = new Node { Name = "Smoke" };
        Assert.Equal("Smoke", (string)node.Name);
        node.Free();
    }
}
