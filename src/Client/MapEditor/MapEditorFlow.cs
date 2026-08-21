using Godot;
using Mortz.Content;
using Mortz.Core.Net;
using Mortz.Shared;

namespace Mortz.Client.MapEditor;

public sealed record ContentPackChoice(ContentPackDefinition Definition, int MapCount);
public sealed record MapChoice(ContentDefinition<MapManifest> Definition);

public partial class MapEditorFlow : Node
{
    private ContentCatalog? _catalog;
    private ContentPackDefinition? _selectedPack;

    public event Action<IReadOnlyList<ContentPackChoice>>? PacksShown;
    public event Action<ContentPackChoice, IReadOnlyList<MapChoice>>? MapsShown;
    public event Action<ContentDefinition<MapManifest>>? MapSelected;
    public event Action<string, bool>? StatusChanged;
    public event Action? Closed;

    public void Start()
    {
        _selectedPack = null;
        ContentCatalogResult result = ContentCatalog.Load(ContentRoot.Resolve());
        _catalog = result.Catalog;
        if (_catalog == null)
        {
            ShowError(result.Diagnostics.Count > 0
                ? string.Join("; ", result.Diagnostics.Select(d => d.Message))
                : "Content packs could not be loaded.");
            PacksShown?.Invoke([]);
            return;
        }

        ContentPackChoice[] packs =
        [
            .. _catalog.Packs
                .Select(pack => new ContentPackChoice(pack, MapsIn(pack).Count))
        ];
        PacksShown?.Invoke(packs);
        StatusChanged?.Invoke("", false);
    }

    public void SelectPack(int index)
    {
        if (_catalog == null || index < 0 || index >= _catalog.Packs.Length)
            return;
        ShowMaps(_catalog.Packs[index]);
    }

    public void SelectMap(int index)
    {
        if (_selectedPack == null)
            return;
        List<MapChoice> maps = MapsIn(_selectedPack);
        if (index >= 0 && index < maps.Count)
            MapSelected?.Invoke(maps[index].Definition);
    }

    public void CreatePack(string name, string id)
    {
        name = name.Trim();
        id = id.Trim().ToLowerInvariant();
        if (name.Length == 0)
        {
            ShowError("Enter a name for the content pack.");
            return;
        }
        if (!IsValidPackId(id))
        {
            ShowError("Use an ID like com.example.my-maps.");
            return;
        }
        if (_catalog?.Packs.Any(pack => pack.Manifest.Id == id) == true)
        {
            ShowError($"The ID '{id}' is already in use.");
            return;
        }

        string directory = Path.Combine(ContentRoot.Resolve(), id.Split('.')[^1]);
        if (Directory.Exists(directory))
        {
            ShowError($"A folder named '{Path.GetFileName(directory)}' already exists.");
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            ContentPackManifest manifest = new(id, name, "1.0.0");
            File.WriteAllText(Path.Combine(directory, "content_pack.toml"),
                TomlModel.Write(manifest));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not create the content pack: {exception.Message}");
            return;
        }

        ReloadAndShowPack(id);
    }

    public void CreateMap(string id, string name, int width, int height, int suggestedPlayers)
    {
        if (_selectedPack == null)
            return;
        id = id.Trim().ToLowerInvariant();
        name = name.Trim();
        if (!ContentId.IsValid(id))
        {
            ShowError("Use only lowercase letters, numbers, hyphens, and underscores for the map ID.");
            return;
        }
        if (name.Length == 0)
        {
            ShowError("Enter a name for the map.");
            return;
        }
        if (MapsIn(_selectedPack).Any(map => map.Definition.Id == id))
        {
            ShowError($"The map ID '{id}' is already in use here.");
            return;
        }
        if (width is < 320 or > 8192 || height is < 240 or > 8192)
        {
            ShowError("Map size must be between 320 x 240 and 8192 x 8192.");
            return;
        }

        try
        {
            Image empty = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            empty.Fill(Colors.Transparent);
            byte[] emptyPng = empty.SavePngToBuffer();
            MapEditorLayerAsset emptyLayer = new(emptyPng, width, height);
            MapEditorLayers layers = new(emptyLayer, emptyLayer, emptyLayer);
            MapEditorBrushDocument brushDocument = MapEditorBrushDocument.Empty(layers);
            MapPackageWriter.Write(Path.Combine(_selectedPack.DirectoryPath, "maps"),
                new MapPackageWriteRequest(
                    id,
                    new MapManifest
                    {
                        Name = name,
                        SuggestedPlayers = Math.Clamp(suggestedPlayers, 1,
                            NetConfig.MAX_PLAYERS),
                    },
                    emptyPng,
                    emptyPng,
                    emptyPng,
                    width,
                    height,
                    new Dictionary<string, ReadOnlyMemory<byte>>
                    {
                        ["editor.json"] = MapEditorDocumentJson.Serialize(brushDocument),
                    }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            ShowError($"Could not create the map: {exception.Message}");
            return;
        }

        string packId = _selectedPack.Manifest.Id;
        ContentCatalogResult result = ContentCatalog.Load(ContentRoot.Resolve());
        _catalog = result.Catalog;
        ContentPackDefinition? pack = _catalog?.Packs.FirstOrDefault(candidate =>
            candidate.Manifest.Id == packId);
        ContentDefinition<MapManifest>? definition = pack == null
            ? null
            : MapsIn(pack).FirstOrDefault(map => map.Definition.Id == id)?.Definition;
        if (definition == null)
        {
            ShowError("Map created, but it couldn't be opened.");
            return;
        }
        _selectedPack = pack;
        MapSelected?.Invoke(definition);
    }

    public void BackToPacks() => Start();
    public void Exit() => Closed?.Invoke();

    private void ReloadAndShowPack(string id)
    {
        ContentCatalogResult result = ContentCatalog.Load(ContentRoot.Resolve());
        _catalog = result.Catalog;
        ContentPackDefinition? pack = _catalog?.Packs.FirstOrDefault(candidate =>
            candidate.Manifest.Id == id);
        if (pack == null)
        {
            ShowError("Content pack created, but it couldn't be opened.");
            return;
        }
        ShowMaps(pack);
    }

    private void ShowMaps(ContentPackDefinition pack)
    {
        _selectedPack = pack;
        List<MapChoice> maps = MapsIn(pack);
        MapsShown?.Invoke(new ContentPackChoice(pack, maps.Count), maps);
        StatusChanged?.Invoke("", false);
    }

    private List<MapChoice> MapsIn(ContentPackDefinition pack) => _catalog?.Maps.Values
        .SelectMany(map => map.OverrideChain)
        .Where(map => map.SourcePack.DirectoryPath == pack.DirectoryPath)
        .OrderBy(map => map.Id, StringComparer.Ordinal)
        .Select(map => new MapChoice(map))
        .ToList() ?? [];

    private static bool IsValidPackId(string id)
    {
        string[] parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts.All(ContentId.IsValid);
    }

    private void ShowError(string message) => StatusChanged?.Invoke(message, true);
}
