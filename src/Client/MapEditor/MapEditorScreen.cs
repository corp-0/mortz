using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

public enum MapEditorPendingAction
{
    NONE,
    RELOAD,
    BACK,
}

[Meta(typeof(IAutoNode))]
public partial class MapEditorScreen : Node2D, IProvide<MapEditorFlow>
{
    [Signal] public delegate void ClosedEventHandler();

    [Export] private MapEditorFlow _flow = null!;
    [Export] private MapEditorFlowHud _flowHud = null!;
    [Export] private MapEditorHud _editorHud = null!;

    private readonly IMapEditorStore _store = new FileMapEditorStore();
    private MapEditorWorkspace? _workspace;
    private MapEditorPendingAction _pendingAction;

    MapEditorFlow IProvide<MapEditorFlow>.Value() => _flow;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        _flow.MapSelected += OpenEditor;
        _flow.Closed += OnFlowClosed;
        _editorHud.SaveRequested += Save;
        _editorHud.ReloadRequested += RequestReload;
        _editorHud.BackRequested += RequestBack;
        _editorHud.DiscardConfirmed += ConfirmDiscard;
        _editorHud.DiscardCancelled += CancelDiscard;
        _editorHud.ZoneAddRequested += AddZone;
        _editorHud.ZoneReplaceRequested += ReplaceZone;
        _editorHud.ZoneRemoveRequested += RemoveZone;
        _editorHud.SpawnAddRequested += AddSpawn;
        _editorHud.SpawnReplaceRequested += ReplaceSpawn;
        _editorHud.SpawnRemoveRequested += RemoveSpawn;
        _editorHud.LayerReplaceRequested += ReplaceLayer;
        this.Provide();
    }

    public void OnExitTree()
    {
        _flow.MapSelected -= OpenEditor;
        _flow.Closed -= OnFlowClosed;
        _editorHud.SaveRequested -= Save;
        _editorHud.ReloadRequested -= RequestReload;
        _editorHud.BackRequested -= RequestBack;
        _editorHud.DiscardConfirmed -= ConfirmDiscard;
        _editorHud.DiscardCancelled -= CancelDiscard;
        _editorHud.ZoneAddRequested -= AddZone;
        _editorHud.ZoneReplaceRequested -= ReplaceZone;
        _editorHud.ZoneRemoveRequested -= RemoveZone;
        _editorHud.SpawnAddRequested -= AddSpawn;
        _editorHud.SpawnReplaceRequested -= ReplaceSpawn;
        _editorHud.SpawnRemoveRequested -= RemoveSpawn;
        _editorHud.LayerReplaceRequested -= ReplaceLayer;
        _workspace = null;
        _pendingAction = MapEditorPendingAction.NONE;
    }

    private void OpenEditor(ContentDefinition<MapManifest> definition)
    {
        _workspace = null;
        _pendingAction = MapEditorPendingAction.NONE;
        _flowHud.HideForEditor();
        _editorHud.ShowForEditor();

        MapEditorOpenResult result = MapEditorWorkspace.Open(definition, _store);
        if (!result.Succeeded)
        {
            ShowFailure("Could not open map", result.Failure);
            return;
        }

        _workspace = result.Workspace;
        _editorHud.Apply(result.Update!);
        _editorHud.ShowStatus(new MapEditorStatus($"Editing {result.Update!.Snapshot.MapId}"));
    }

    private void AddZone(MapEditorZoneDraft zone)
    {
        if (_workspace != null)
            ApplyEdit(_workspace.AddZone(zone));
    }

    private void ReplaceZone(MapEditorZoneId id, MapEditorZoneDraft zone)
    {
        if (_workspace != null)
            ApplyEdit(_workspace.ReplaceZone(id, zone));
    }

    private void RemoveZone(MapEditorZoneId id)
    {
        if (_workspace != null)
            ApplyEdit(_workspace.RemoveZone(id));
    }

    private void AddSpawn(MapSpawnPoint spawn)
    {
        if (_workspace != null)
            ApplyEdit(_workspace.AddSpawn(spawn));
    }

    private void ReplaceSpawn(MapEditorSpawnId id, MapSpawnPoint spawn)
    {
        if (_workspace != null)
            ApplyEdit(_workspace.ReplaceSpawn(id, spawn));
    }

    private void RemoveSpawn(MapEditorSpawnId id)
    {
        if (_workspace != null)
            ApplyEdit(_workspace.RemoveSpawn(id));
    }

    private void ReplaceLayer(MapEditorLayer layer, string path)
    {
        if (_workspace == null)
            return;
        MapEditorOperationResult result = _workspace.ReplaceLayer(layer, path);
        if (result.Failure != null)
        {
            ShowFailure($"Could not replace {LayerName(layer).ToLowerInvariant()} image",
                result.Failure);
            return;
        }
        if (result.Update != null)
        {
            _editorHud.Apply(result.Update);
            _editorHud.ShowStatus(new MapEditorStatus($"{LayerName(layer)} image replaced"));
        }
    }

    private void Save()
    {
        if (_workspace?.Snapshot.CanSave != true)
            return;
        MapEditorOperationResult result = _workspace.Save();
        if (result.Failure != null)
        {
            ShowFailure("Save failed", result.Failure);
            return;
        }
        if (result.Update != null)
        {
            _editorHud.Apply(result.Update);
            _editorHud.ShowStatus(new MapEditorStatus($"Saved {result.Update.Snapshot.MapId}"));
        }
    }

    private void RequestReload()
    {
        if (_workspace == null)
            return;
        if (_workspace.Snapshot.Dirty)
        {
            RequestDiscard(MapEditorPendingAction.RELOAD);
            return;
        }
        Reload();
    }

    private void RequestBack()
    {
        if (_workspace?.Snapshot.Dirty == true)
        {
            RequestDiscard(MapEditorPendingAction.BACK);
            return;
        }
        ReturnToFlow();
    }

    private void RequestDiscard(MapEditorPendingAction action)
    {
        _pendingAction = action;
        _editorHud.ShowDiscardConfirmation();
    }

    private void ConfirmDiscard()
    {
        MapEditorPendingAction action = _pendingAction;
        _pendingAction = MapEditorPendingAction.NONE;
        switch (action)
        {
            case MapEditorPendingAction.RELOAD:
                Reload();
                break;
            case MapEditorPendingAction.BACK:
                ReturnToFlow();
                break;
        }
    }

    private void CancelDiscard() => _pendingAction = MapEditorPendingAction.NONE;

    private void Reload()
    {
        if (_workspace == null)
            return;
        MapEditorOperationResult result = _workspace.Reload();
        if (result.Failure != null)
        {
            ShowFailure("Reload failed", result.Failure);
            return;
        }
        if (result.Update != null)
        {
            _editorHud.Apply(result.Update);
            _editorHud.ShowStatus(new MapEditorStatus($"Reloaded {result.Update.Snapshot.MapId}"));
        }
    }

    private void ApplyEdit(MapEditorUpdate? update)
    {
        if (update != null)
            _editorHud.Apply(update);
    }

    private void ReturnToFlow()
    {
        _workspace = null;
        _pendingAction = MapEditorPendingAction.NONE;
        _editorHud.HideForFlow();
        _flow.Start();
    }

    private void ShowFailure(string prefix, MapEditorOperationFailure? failure)
    {
        string detail = failure switch
        {
            MapEditorContentFailure content when !content.Diagnostics.IsEmpty =>
                string.Join("; ", content.Diagnostics.Select(diagnostic => diagnostic.Message)),
            MapEditorIoFailure io => io.Message,
            MapEditorInvalidPngFailure png => $"'{png.Path}' is not a valid PNG ({png.Reason}).",
            MapEditorLayerSizeFailure size =>
                $"Layer must be {size.ExpectedWidth} x {size.ExpectedHeight} px; " +
                $"this image is {size.ActualWidth} x {size.ActualHeight} px.",
            _ => "The operation failed.",
        };
        _editorHud.ShowStatus(new MapEditorStatus($"{prefix}: {detail}", true));
    }

    private void OnFlowClosed() => EmitSignal(SignalName.Closed);

    private static string LayerName(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => "Background",
        MapEditorLayer.SOLID => "Solid",
        MapEditorLayer.DESTRUCTIBLE => "Destructible",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };
}
