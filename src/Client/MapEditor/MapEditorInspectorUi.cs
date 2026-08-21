using Godot;

namespace Mortz.Client.MapEditor;

public static class MapEditorInspectorUi
{
    public static readonly Color Text = new("e2eaf3");
    public static readonly Color MutedText = new("93a4b7");
    public static readonly Color Danger = new("ff8b82");
    public static readonly Color Success = new("83d5ac");
    public static readonly Color Warning = new("ffb0c8");

    public static void Metadata(Label label, Color? color = null)
    {
        label.AddThemeColorOverride("font_color", color ?? MutedText);
        label.AddThemeFontSizeOverride("font_size", 12);
    }
}
