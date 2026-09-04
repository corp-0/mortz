using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorStampDock : ScrollContainer
{
    [Export] private MapEditorStampLibrary _library = null!;

    public MapEditorStampLibrary Library => _library;

    public override void _Ready()
    {
        Resized += FitLibrary;
        _library.MinimumSizeChanged += FitLibrary;
        FitLibrary();
    }

    public override void _ExitTree()
    {
        Resized -= FitLibrary;
        _library.MinimumSizeChanged -= FitLibrary;
    }

    private void FitLibrary()
    {
        // Reserve the scrollbar width even before overflow so columns don't oscillate.
        _library.FitColumns(Size.X - GetVScrollBar().GetCombinedMinimumSize().X);
    }
}
