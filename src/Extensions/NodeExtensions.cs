using Godot;

namespace Mortz.Extensions;

public static class NodeExtensions
{
    public static T? GetChildByTypeOrNull<T>(this Node node,
        Func<T, bool>? predicate = null) where T : class
    {
        Node? validNode = node.OrNull();
        if (validNode is null)
            return null;

        foreach (Node child in validNode.GetChildren())
        {
            if (child.OrNull() is T match && (predicate is null || predicate(match)))
                return match;
        }

        return null;
    }

    public static T GetDescendantByType<T>(this Node node,
        Func<T, bool>? predicate = null) where T : class
    {
        T? match = node.GetDescendantByTypeOrNull(predicate);
        string predicateText = predicate is null ? "" : " matching the predicate";
        return match ?? throw new InvalidOperationException(
            $"No descendant of type {typeof(T).Name}{predicateText} exists.");
    }

    public static T? GetDescendantByTypeOrNull<T>(this Node node,
        Func<T, bool>? predicate = null) where T : class
    {
        Node? validNode = node.OrNull();
        return validNode is null
            ? null
            : FindDescendantByTypeOrNull(validNode, predicate);
    }

    public static IEnumerable<T> GetDescendantsByType<T>(this Node node,
        Func<T, bool>? predicate = null) where T : class
    {
        Node? validNode = node.OrNull();
        if (validNode is null)
            yield break;

        foreach (Node child in validNode.GetChildren())
        {
            Node? validChild = child.OrNull();
            if (validChild is null)
                continue;
            if (validChild is T match && (predicate is null || predicate(match)))
                yield return match;

            foreach (T descendant in validChild.GetDescendantsByType(predicate))
                yield return descendant;
        }
    }

    private static T? FindDescendantByTypeOrNull<T>(Node node,
        Func<T, bool>? predicate) where T : class
    {
        foreach (Node child in node.GetChildren())
        {
            Node? validChild = child.OrNull();
            if (validChild is null)
                continue;
            if (validChild is T match && (predicate is null || predicate(match)))
                return match;

            T? descendant = FindDescendantByTypeOrNull(validChild, predicate);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }

    public static void KillDescendants(this Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            child.QueueFree();
        }
    }
}
