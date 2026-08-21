using System.Collections.Immutable;
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
    [Signal]
    public delegate void ClosedEventHandler();

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
        _editorHud.ZoneDuplicateRequested += DuplicateZone;
        _editorHud.SpawnAddRequested += AddSpawn;
        _editorHud.SpawnReplaceRequested += ReplaceSpawn;
        _editorHud.SpawnRemoveRequested += RemoveSpawn;
        _editorHud.SpawnDuplicateRequested += DuplicateSpawn;
        _editorHud.BrushSourceInitializationRequested += InitializeBrushSource;
        _editorHud.BrushAddRequested += AddBrush;
        _editorHud.BrushBatchAddRequested += AddBrushes;
        _editorHud.BrushBatchRemoveRequested += RemoveBrushes;
        _editorHud.BrushReplaceRequested += ReplaceBrush;
        _editorHud.BrushRemoveRequested += RemoveBrush;
        _editorHud.BrushDuplicateRequested += DuplicateBrush;
        _editorHud.BrushReorderRequested += ReorderBrush;
        _editorHud.BrushMoveToLayerRequested += MoveBrushToLayer;
        _editorHud.StampSaveRequested += SaveStamp;
        _editorHud.StampRemoveRequested += RemoveStamp;
        _editorHud.UndoRequested += Undo;
        _editorHud.RedoRequested += Redo;
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
        _editorHud.ZoneDuplicateRequested -= DuplicateZone;
        _editorHud.SpawnAddRequested -= AddSpawn;
        _editorHud.SpawnReplaceRequested -= ReplaceSpawn;
        _editorHud.SpawnRemoveRequested -= RemoveSpawn;
        _editorHud.SpawnDuplicateRequested -= DuplicateSpawn;
        _editorHud.BrushSourceInitializationRequested -= InitializeBrushSource;
        _editorHud.BrushAddRequested -= AddBrush;
        _editorHud.BrushBatchAddRequested -= AddBrushes;
        _editorHud.BrushBatchRemoveRequested -= RemoveBrushes;
        _editorHud.BrushReplaceRequested -= ReplaceBrush;
        _editorHud.BrushRemoveRequested -= RemoveBrush;
        _editorHud.BrushDuplicateRequested -= DuplicateBrush;
        _editorHud.BrushReorderRequested -= ReorderBrush;
        _editorHud.BrushMoveToLayerRequested -= MoveBrushToLayer;
        _editorHud.StampSaveRequested -= SaveStamp;
        _editorHud.StampRemoveRequested -= RemoveStamp;
        _editorHud.UndoRequested -= Undo;
        _editorHud.RedoRequested -= Redo;
        _workspace = null;
        _pendingAction = MapEditorPendingAction.NONE;
    }

    private void OpenEditor(ContentDefinition<MapManifest> definition)
    {
        _workspace = null;
        _pendingAction = MapEditorPendingAction.NONE;
        _flowHud.HideForEditor();
        _editorHud.ShowForEditor();

        MapEditorTextureSourceRegistry textureSources =
            MapEditorTextureSourceRegistry.CreateDefault(definition);
        MapEditorTextureResolver textureResolver = new(textureSources);
        MapEditorOpenResult result = MapEditorWorkspace.Open(definition, _store, textureResolver);
        if (!result.Succeeded)
        {
            ShowFailure("Could not open map", result.Failure);
            return;
        }

        _workspace = result.Workspace;
        _editorHud.ConfigureTextureSources(textureResolver, textureSources);
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

    private void DuplicateZone(MapEditorZoneId id, int offset)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.DuplicateZone(id, offset), "Could not duplicate zone");
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

    private void DuplicateSpawn(MapEditorSpawnId id, int offset)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.DuplicateSpawn(id, offset), "Could not duplicate spawn");
    }

    private void InitializeBrushSource()
    {
        if (_workspace == null)
            return;
        MapEditorOperationResult result = _workspace.InitializeBrushSource();
        if (result.Failure != null)
        {
            ShowFailure("Could not enable layer editing", result.Failure);
            return;
        }

        if (result.Update != null)
        {
            _editorHud.Apply(result.Update);
            _editorHud.ShowStatus(new MapEditorStatus(
                "Layer editing enabled. Existing images remain as a reference until you save."));
        }
    }

    private void AddBrush(MapEditorBrushDraft draft)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.AddBrush(draft), "Could not add brush");
    }

    private void AddBrushes(ImmutableArray<MapEditorBrushDraft> drafts)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.AddBrushes(drafts), "Could not paint stamp stroke");
    }

    private void RemoveBrushes(ImmutableArray<MapEditorBrushId> ids)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.RemoveBrushes(ids.ToHashSet()),
                "Could not erase stamp stroke");
    }

    private void ReplaceBrush(MapEditorBrushId id, MapEditorBrushDraft draft)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.ReplaceBrush(id, draft), "Could not edit brush");
    }

    private void RemoveBrush(MapEditorBrushId id)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.RemoveBrush(id), "Could not delete brush");
    }

    private void DuplicateBrush(MapEditorBrushId id, int offset)
    {
        if (_workspace == null)
            return;
        MapEditorOperationResult result = _workspace.DuplicateBrush(id, offset);
        ApplyOperation(result, "Could not duplicate brush");
        if (result.Update?.Change is MapEditorBrushAdded added &&
            TryFindBrushLayer(added.Id, out MapEditorLayer layer))
        {
            _editorHud.SelectBrush(layer, added.Id);
        }
    }

    private void MoveBrushToLayer(MapEditorBrushId id, MapEditorLayer destination)
    {
        if (_workspace == null)
            return;
        MapEditorOperationResult result = _workspace.MoveBrushToLayer(id, destination);
        ApplyOperation(result, "Could not move brush");
        if (result.Update != null)
            _editorHud.SelectBrush(destination, id);
    }

    private void SaveStamp(MapEditorBrushId id)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.SaveStamp(id), "Could not save stamp");
    }

    private void RemoveStamp(MapEditorStampId id)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.RemoveStamp(id), "Could not remove stamp");
    }

    private bool TryFindBrushLayer(MapEditorBrushId id, out MapEditorLayer layer)
    {
        if (_workspace?.Snapshot.BrushDocument != null)
        {
            foreach (MapEditorLayer candidate in Enum.GetValues<MapEditorLayer>())
            {
                if (_workspace.Snapshot.BrushDocument.Layers.Get(candidate).Brushes.Any(brush => brush.Id == id))
                {
                    layer = candidate;
                    return true;
                }
            }
        }

        layer = default;
        return false;
    }

    private void ReorderBrush(MapEditorBrushId id, int destinationIndex)
    {
        if (_workspace != null)
            ApplyOperation(_workspace.ReorderBrush(id, destinationIndex), "Could not reorder brush");
    }

    private void ApplyOperation(MapEditorOperationResult result, string failurePrefix)
    {
        if (result.Failure != null)
        {
            ShowFailure(failurePrefix, result.Failure);
            return;
        }

        if (result.Update != null)
            _editorHud.Apply(result.Update);
    }

    private void Undo()
    {
        if (_workspace != null)
            ApplyOperation(_workspace.Undo(), "Could not undo");
    }

    private void Redo()
    {
        if (_workspace != null)
            ApplyOperation(_workspace.Redo(), "Could not redo");
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
            MapEditorUnresolvedBrushesFailure unresolved => string.Join("; ",
                unresolved.Brushes.Select(brush =>
                    $"Brush '{brush.Name}': {brush.Message}")),
            MapEditorCompositionFailure composition =>
                $"Could not build the {composition.Layer.ToString().ToLowerInvariant()} layer: " +
                composition.Message,
            MapEditorIdentityOverflowFailure overflow =>
                $"This map has reached the {overflow.ObjectType} limit.",
            _ => "Something went wrong.",
        };
        _editorHud.ShowStatus(new MapEditorStatus($"{prefix}: {detail}", true));
    }

    private void OnFlowClosed() => EmitSignal(SignalName.Closed);
}
