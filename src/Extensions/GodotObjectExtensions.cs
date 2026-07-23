using Godot;

namespace Mortz.Extensions;

public static class GodotObjectExtensions
{
    /// <summary>Null when the object is null or already freed.</summary>
    public static T? OrNull<T>(this T? obj) where T : GodotObject =>
        GodotObject.IsInstanceValid(obj) ? obj : null;
}
