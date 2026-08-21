using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorStampDock : ScrollContainer
{
    [Export] private PackedScene _libraryScene = null!;

    public MapEditorStampLibrary Library { get; private set; } = null!;

    public override void _Ready()
    {
        if (_libraryScene == null)
            throw new InvalidOperationException("StampDock requires its library scene binding.");
        Library = _libraryScene.Instantiate<MapEditorStampLibrary>();
        AddChild(Library);
    }
}
