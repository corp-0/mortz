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
            ShowError("Use a reverse-domain ID such as com.example.my-pack.");
            return;
        }
        if (_catalog?.Packs.Any(pack => pack.Manifest.Id == id) == true)
        {
            ShowError($"A content pack with ID '{id}' already exists.");
            return;
        }

        string directory = Path.Combine(ContentRoot.Resolve(), id.Split('.')[^1]);
        if (Directory.Exists(directory))
        {
            ShowError($"The content directory '{Path.GetFileName(directory)}' already exists.");
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
            ShowError("Use lowercase letters, numbers, hyphens, or underscores for the map ID.");
            return;
        }
        if (name.Length == 0)
        {
            ShowError("Enter a name for the map.");
            return;
        }
        if (MapsIn(_selectedPack).Any(map => map.Definition.Id == id))
        {
            ShowError($"This content pack already contains a map with ID '{id}'.");
            return;
        }
        if (width is < 320 or > 8192 || height is < 240 or > 8192)
        {
            ShowError("Map dimensions must be between 320 x 240 and 8192 x 8192.");
            return;
        }

        try
        {
            Image background = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            background.Fill(new Color(0.025f, 0.03f, 0.04f));
            Image empty = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            empty.Fill(Colors.Transparent);
            MapPackageWriter.Write(Path.Combine(_selectedPack.DirectoryPath, "maps"),
                new MapPackageWriteRequest(
                    id,
                    new MapManifest
                    {
                        Name = name,
                        SuggestedPlayers = Math.Clamp(suggestedPlayers, 1,
                            NetConfig.MAX_PLAYERS),
                    },
                    background.SavePngToBuffer(),
                    empty.SavePngToBuffer(),
                    empty.SavePngToBuffer(),
                    width,
                    height));
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
            ShowError("The map was created but could not be loaded.");
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
            ShowError("The content pack was created but could not be loaded.");
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
