using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

[Meta(typeof(IAutoNode))]
public partial class MapEditorScreen : Node2D, IProvide<MapEditor>, IProvide<MapEditorFlow>
{
    [Signal] public delegate void ClosedEventHandler();

    [Export] private MapEditor _editor = null!;
    [Export] private MapEditorFlow _flow = null!;
    [Export] private MapEditorFlowHud _flowHud = null!;
    [Export] private MapEditorHud _editorHud = null!;

    MapEditor IProvide<MapEditor>.Value() => _editor;
    MapEditorFlow IProvide<MapEditorFlow>.Value() => _flow;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _flow.MapSelected += OpenEditor;
        _flow.Closed += OnFlowClosed;
        _editor.BackRequested += ReturnToStart;
        this.Provide();
    }

    public void OnExitTree()
    {
        _flow.MapSelected -= OpenEditor;
        _flow.Closed -= OnFlowClosed;
        _editor.BackRequested -= ReturnToStart;
    }

    private void OpenEditor(ContentDefinition<MapManifest> definition)
    {
        _flowHud.HideForEditor();
        _editorHud.ShowForEditor();
        _editor.Open(definition);
    }

    private void ReturnToStart()
    {
        _editorHud.HideForFlow();
        _flow.Start();
    }

    private void OnFlowClosed() => EmitSignal(SignalName.Closed);
}
