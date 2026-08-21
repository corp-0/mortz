using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorObjectBrowserBrushRow : VBoxContainer
{
    [Export] private Button _select = null!;
    [Export] private CheckButton _visibility = null!;
    [Export] private HBoxContainer _order = null!;
    [Export] private Button _lower = null!;
    [Export] private Button _raise = null!;
    [Export] private Label _details = null!;

    public Button SelectButton => _select;
    public CheckButton VisibilityButton => _visibility;
    public HBoxContainer OrderControls => _order;
    public Button LowerButton => _lower;
    public Button RaiseButton => _raise;
    public Label DetailsLabel => _details;
}
