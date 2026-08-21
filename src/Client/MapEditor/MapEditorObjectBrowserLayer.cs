using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorObjectBrowserLayer : VBoxContainer
{
    [Export] private Button _expand = null!;
    [Export] private Button _select = null!;
    [Export] private CheckButton _visibility = null!;
    [Export] private Label _summary = null!;
    [Export] private VBoxContainer _brushes = null!;
    [Export] private Label _empty = null!;

    public Button ExpandButton => _expand;
    public Button SelectButton => _select;
    public CheckButton VisibilityButton => _visibility;
    public Label SummaryLabel => _summary;
    public VBoxContainer Brushes => _brushes;
    public Label EmptyLabel => _empty;
}
