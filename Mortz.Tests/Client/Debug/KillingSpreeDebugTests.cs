using System.Reflection;
using Godot;
using Mortz.Client.Debug;
using Xunit;

namespace Mortz.Tests.Client.Debug;

[Collection(nameof(MortzGodotCollection))]
public sealed class KillingSpreeDebugTests
{
    [Fact]
    public void SceneEntersTheTreeWithoutMatchServices()
    {
        KillingSpreeDebug preview = Instantiate();
        Window root = ((SceneTree)Engine.GetMainLoop()).Root;

        root.AddChild(preview);
        root.RemoveChild(preview);
        preview.Free();
    }

    [Fact]
    public void SceneExportsAreWired()
    {
        KillingSpreeDebug preview = Instantiate();

        foreach (FieldInfo field in typeof(KillingSpreeDebug).GetFields(
                     BindingFlags.Instance | BindingFlags.NonPublic)
                 .Where(field => field.IsDefined(typeof(ExportAttribute))))
        {
            Assert.NotNull(field.GetValue(preview));
        }
        preview.Free();
    }

    private static KillingSpreeDebug Instantiate() =>
        ResourceLoader.Load<PackedScene>(
                "res://src/Shared/Scenes/Debug/KillingSpreeDebug.tscn")
            .Instantiate<KillingSpreeDebug>();
}
