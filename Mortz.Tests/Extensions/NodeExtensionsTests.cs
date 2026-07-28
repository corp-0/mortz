using Godot;
using Mortz.Extensions;
using Xunit;

namespace Mortz.Tests.Extensions;

[Collection(nameof(MortzGodotCollection))]
public class NodeExtensionsTests
{
    [Fact]
    public void PredicateSelectsTheSemanticMatch()
    {
        Node root = new();
        Node branch = new();
        root.AddChild(new Button { Text = "SKIP" });
        root.AddChild(branch);
        Button join = new() { Text = "JOIN" };
        branch.AddChild(join);

        try
        {
            Button match = root.GetDescendantByType<Button>(
                button => button.Text == "JOIN");
            Assert.Same(join, match);
        }
        finally
        {
            root.Free();
        }
    }

    [Fact]
    public void OrNullLookupsRejectAFreedReceiver()
    {
        Node root = new();
        root.Free();

        Assert.Null(root.GetChildByTypeOrNull<Node>());
        Assert.Null(root.GetDescendantByTypeOrNull<Node>());
        Assert.Empty(root.GetDescendantsByType<Node>());
    }
}
