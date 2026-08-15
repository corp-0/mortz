using Godot;
using Mortz.Content;
using Mortz.Shared;

namespace Mortz.Client.MapEditor;

public enum MapEditorPendingAction
{
    NONE,
    RELOAD,
    BACK,
}

public enum MapEditorLayer
{
    BACKGROUND,
    SOLID,
    DESTRUCTIBLE,
}

public sealed record MapEditorMapLoaded(
    Image Background,
    Image Solid,
    Image Destructible,
    MapEditorDocument Document);

public partial class MapEditor : Node
{
    private ContentDefinition<MapManifest>? _definition;
    private MapEditorPendingAction _pendingAction;
    private Image? _background;
    private Image? _solid;
    private Image? _destructible;

    public event Action? BackRequested;
    public event Action? DiscardRequested;
    public event Action<MapEditorMapLoaded>? MapLoaded;
    public event Action<MapEditorLayer, Image>? LayerChanged;
    public event Action<string, bool>? StatusChanged;
    public event Action<bool>? DirtyChanged;

    public MapEditorDocument? Document { get; private set; }
    public bool Dirty { get; private set; }

    public void Open(ContentDefinition<MapManifest> definition)
    {
        _definition = definition;
        LoadMap();
    }

    public void Reload()
    {
        if (Dirty)
            RequestDiscard(MapEditorPendingAction.RELOAD);
        else
            LoadMap();
    }

    public void Close()
    {
        if (Dirty)
            RequestDiscard(MapEditorPendingAction.BACK);
        else
            BackRequested?.Invoke();
    }

    public void MarkChanged()
    {
        Dirty = true;
        DirtyChanged?.Invoke(true);
        SetStatus("Unsaved changes");
    }

    public void ReplaceLayer(MapEditorLayer layer, string path)
    {
        if (_background == null || _solid == null || _destructible == null)
            return;

        Image image = new();
        Error error = image.Load(path);
        if (error != Error.Ok)
        {
            SetStatus($"Could not open that PNG ({error}).", true);
            return;
        }
        if (image.GetWidth() != _background.GetWidth() ||
            image.GetHeight() != _background.GetHeight())
        {
            SetStatus($"Layer must be {_background.GetWidth()} x {_background.GetHeight()} px; " +
                $"this image is {image.GetWidth()} x {image.GetHeight()} px.", true);
            return;
        }

        switch (layer)
        {
            case MapEditorLayer.BACKGROUND:
                _background = image;
                break;
            case MapEditorLayer.SOLID:
                _solid = image;
                break;
            case MapEditorLayer.DESTRUCTIBLE:
                _destructible = image;
                break;
        }
        MarkChanged();
        LayerChanged?.Invoke(layer, image);
        SetStatus($"{LayerName(layer)} image replaced");
    }

    public void Save()
    {
        if (_definition == null || Document == null || _background == null ||
            _solid == null || _destructible == null)
        {
            return;
        }

        try
        {
            MapManifest manifest = Document.BuildManifest();
            string mapsDirectory = Directory.GetParent(_definition.DirectoryPath)!.FullName;
            MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
                _definition.Id,
                manifest,
                _background.SavePngToBuffer(),
                _solid.SavePngToBuffer(),
                _destructible.SavePngToBuffer(),
                _solid.GetWidth(),
                _solid.GetHeight()));
            _definition = _definition with { Manifest = manifest };
            SetDirty(false);
            SetStatus($"Saved {_definition.Id}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            SetStatus($"Save failed: {exception.Message}", true);
        }
    }

    public void ConfirmDiscard()
    {
        MapEditorPendingAction action = _pendingAction;
        _pendingAction = MapEditorPendingAction.NONE;
        SetDirty(false);
        switch (action)
        {
            case MapEditorPendingAction.RELOAD:
                LoadMap();
                break;
            case MapEditorPendingAction.BACK:
                BackRequested?.Invoke();
                break;
        }
    }

    private void LoadMap()
    {
        if (_definition == null)
            return;
        MapPackageLoadResult result = MapPackageLoader.Load(_definition);
        if (result.Package == null)
        {
            SetStatus(string.Join("; ", result.Diagnostics.Select(d => d.Message)), true);
            return;
        }

        Document = new MapEditorDocument(_definition.Manifest);
        _background = result.Package.Background;
        _solid = result.Package.Solid;
        _destructible = result.Package.Destructible;
        SetDirty(false);
        MapLoaded?.Invoke(new MapEditorMapLoaded(
            _background,
            _solid,
            _destructible,
            Document));
        SetStatus($"Editing {_definition.Id}");
    }

    private void RequestDiscard(MapEditorPendingAction action)
    {
        _pendingAction = action;
        DiscardRequested?.Invoke();
    }

    private void SetDirty(bool dirty)
    {
        Dirty = dirty;
        DirtyChanged?.Invoke(dirty);
    }

    private void SetStatus(string text, bool error = false)
    {
        string display = Dirty && !text.StartsWith("Unsaved", StringComparison.Ordinal)
            ? $"{text} - unsaved changes"
            : text;
        StatusChanged?.Invoke(display, error);
    }

    private static string LayerName(MapEditorLayer layer) => layer switch
    {
        MapEditorLayer.BACKGROUND => "Background",
        MapEditorLayer.SOLID => "Solid",
        MapEditorLayer.DESTRUCTIBLE => "Destructible",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };
}
