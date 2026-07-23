using Godot;

namespace Mortz.Extensions;

public static class NodeExtensions
{
    public static T? GetByTypeOrNull<T>(this Node node) where T : class
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T match)
                return match;
        }

        return null;
    }
}
