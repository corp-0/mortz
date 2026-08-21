using Godot;

namespace Mortz.Client.MapEditor;

public enum MapEditorViewLayer
{
    BACKGROUND,
    SOLID,
    DESTRUCTIBLE,
    ZONES,
    SPAWNS,
    GRID,
}

[GlobalClass]
public partial class MapEditorViewControls : HBoxContainer
{
    [Export] private OptionButton _snap = null!;
    [Export] private MenuButton _view = null!;

    public event Action<MapEditorSnap>? SnapSelected;
    public event Action<MapEditorViewLayer, bool>? ViewVisibilityChanged;
    public event Action? ResetZoomRequested;
    public event Action? FrameMapRequested;

    public override void _Ready()
    {
        _snap.AddItem("Snap: none");
        _snap.AddItem("Snap: 8");
        _snap.AddItem("Snap: 16");
        _snap.AddItem("Snap: 32");
        _snap.ItemSelected += OnSnapSelected;
        ConfigureViewMenu();
        _snap.FocusNext = _snap.GetPathTo(_view);
        _view.FocusPrevious = _view.GetPathTo(_snap);
    }

    public void ApplySnap(MapEditorSnap snap)
    {
        _snap.Select(snap switch
        {
            MapEditorSnap.NONE => 0,
            MapEditorSnap.PIXELS_8 => 1,
            MapEditorSnap.PIXELS_16 => 2,
            _ => 3,
        });
        _snap.TooltipText =
            $"Snap: {(snap == MapEditorSnap.NONE ? "off" : $"{(int)snap} px")}. " +
            "Press G to change it or Shift+G to show the grid.";
    }

    public void ApplyViewVisibility(MapEditorViewLayer layer, bool visible)
    {
        PopupMenu popup = _view.GetPopup();
        int index = popup.GetItemIndex(ViewId(layer));
        if (index >= 0)
        {
            popup.SetItemChecked(index, visible);
        }
    }

    private void OnSnapSelected(long index) => SnapSelected?.Invoke(index switch
    {
        0 => MapEditorSnap.NONE,
        1 => MapEditorSnap.PIXELS_8,
        2 => MapEditorSnap.PIXELS_16,
        _ => MapEditorSnap.PIXELS_32,
    });

    private void ConfigureViewMenu()
    {
        PopupMenu popup = _view.GetPopup();
        foreach ((MapEditorViewLayer layer, string text) in new[]
                 {
                     (MapEditorViewLayer.BACKGROUND, "Background"),
                     (MapEditorViewLayer.SOLID, "Solid"),
                     (MapEditorViewLayer.DESTRUCTIBLE, "Destructible"),
                     (MapEditorViewLayer.ZONES, "Zones"),
                     (MapEditorViewLayer.SPAWNS, "Spawns"),
                     (MapEditorViewLayer.GRID, "Grid"),
                 })
        {
            popup.AddCheckItem(text, ViewId(layer));
            popup.SetItemChecked(popup.ItemCount - 1, true);
        }

        popup.AddSeparator();
        popup.AddItem("Reset zoom", 7);
        popup.AddItem("Frame map", 8);
        popup.IdPressed += OnViewItemPressed;
    }

    private void OnViewItemPressed(long id)
    {
        if (id is >= 1 and <= 6)
        {
            PopupMenu popup = _view.GetPopup();
            int index = popup.GetItemIndex((int)id);
            bool visible = !popup.IsItemChecked(index);
            popup.SetItemChecked(index, visible);
            ViewVisibilityChanged?.Invoke((MapEditorViewLayer)(id - 1), visible);
        }
        else if (id == 7)
        {
            ResetZoomRequested?.Invoke();
        }
        else if (id == 8)
        {
            FrameMapRequested?.Invoke();
        }
    }

    private static int ViewId(MapEditorViewLayer layer) => (int)layer + 1;
}
