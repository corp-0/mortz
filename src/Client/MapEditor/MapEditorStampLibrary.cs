using System.Collections.Immutable;
using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorStampLibrary : GridContainer
{
    [Export] private PackedScene _cardScene = null!;
    private MapEditorSnapshot? _snapshot;
    private MapEditorBrushId? _selectedBrush;
    private MapEditorStampId? _selectedStamp;
    private ImmutableArray<MapEditorStamp> _stamps = [];
    private MapEditorStampCard? _saveCard;
    private readonly Dictionary<MapEditorStampId, MapEditorStampCard> _stampCards = [];
    private MapEditorCanvasResources _previewResources =
        new(new MapEditorTextureResolver());

    public event Action<MapEditorBrushId>? SaveSelectedRequested;
    public event Action<MapEditorStamp>? StampSelected;
    public event Action<MapEditorStampId>? StampRemoveRequested;

    public override void _ExitTree()
    {
        _previewResources.Dispose();
    }

    public void ConfigureTextureResolver(IMapEditorTextureResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _previewResources.Dispose();
        _previewResources = new MapEditorCanvasResources(resolver);
        if (_saveCard != null)
            Rebuild();
    }

    public void Apply(MapEditorSnapshot snapshot, MapEditorBrushId? selectedBrush,
        MapEditorStampId? selectedStamp)
    {
        ImmutableArray<MapEditorStamp> stamps = snapshot.BrushDocument?.Stamps is { } stored &&
                                                 !stored.IsDefault
            ? stored
            : [];
        bool stampsChanged = !_stamps.Equals(stamps);
        bool saveChanged = _selectedBrush != selectedBrush || _snapshot?.CanEditBrushes !=
            snapshot.CanEditBrushes;
        bool selectionChanged = _selectedStamp != selectedStamp;
        _snapshot = snapshot;
        _selectedBrush = selectedBrush;
        _selectedStamp = selectedStamp;
        _stamps = stamps;
        if (stampsChanged || _saveCard == null)
        {
            Rebuild();
            return;
        }
        if (saveChanged)
            ApplySaveAction();
        if (selectionChanged)
            ApplySelection();
    }

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _stampCards.Clear();

        _saveCard = _cardScene.Instantiate<MapEditorStampCard>();
        ApplySaveAction();
        _saveCard.Selected += () =>
        {
            if (_selectedBrush is { } id)
                SaveSelectedRequested?.Invoke(id);
        };
        AddChild(_saveCard);

        if (_stamps.IsEmpty)
        {
            MapEditorStampCard empty = _cardScene.Instantiate<MapEditorStampCard>();
            empty.ApplyEmptyState(_previewResources);
            AddChild(empty);
            return;
        }
        foreach (MapEditorStamp stamp in _stamps)
        {
            MapEditorStampCard card = _cardScene.Instantiate<MapEditorStampCard>();
            card.Apply(stamp, stamp.Id == _selectedStamp, _previewResources);
            card.Selected += () =>
            {
                _selectedStamp = stamp.Id;
                StampSelected?.Invoke(stamp);
                ApplySelection();
            };
            card.DeleteRequested += () => StampRemoveRequested?.Invoke(stamp.Id);
            _stampCards.Add(stamp.Id, card);
            AddChild(card);
        }
    }

    private void ApplySaveAction() => _saveCard?.ApplySaveAction(
        _selectedBrush != null && _snapshot?.CanEditBrushes == true, _previewResources);

    private void ApplySelection()
    {
        foreach (MapEditorStamp stamp in _stamps)
        {
            if (_stampCards.TryGetValue(stamp.Id, out MapEditorStampCard? card))
                card.Apply(stamp, stamp.Id == _selectedStamp, _previewResources);
        }
    }
}
