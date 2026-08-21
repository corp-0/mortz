using Godot;
using Mortz.Client.MapEditor;
using Mortz.Content;
using Xunit;

namespace Mortz.Tests.Client.MapEditor;

[Collection(nameof(MortzGodotCollection))]
public sealed class MapEditorTextureResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"mortz-texture-library-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesProjectAndNamedLibraryReferencesThroughOneContract()
    {
        FakeAccess access = new();
        MapEditorTextureReference project = MapEditorTextureReference.Project("textures/wall.png");
        MapEditorTextureReference library = MapEditorTextureReference.Library(
            "org.example.assets", "terrain/rock.png");
        access.Loads[project] = Resolved(11);
        access.Loads[library] = Resolved(22);
        MapEditorTextureResolver resolver = new(access);

        Assert.True(resolver.Resolve(project).IsResolved);
        Assert.True(resolver.Resolve(library).IsResolved);
        Assert.Equal("res://textures/wall.png", resolver.Resolve(project).ResolvedPath);
        Assert.Equal("library://org.example.assets/terrain/rock.png",
            resolver.Resolve(library).ResolvedPath);
    }

    [Fact]
    public void DistinguishesMissingAndLoadError()
    {
        FakeAccess access = new();
        MapEditorTextureReference broken = MapEditorTextureReference.Project("broken.png");
        access.Loads[broken] = new MapEditorTextureLoadResult(
            MapEditorTextureLoadStatus.LOAD_ERROR, Message: "decode failed");
        MapEditorTextureResolver resolver = new(access);

        Assert.Equal(MapEditorTextureResolutionStatus.MISSING,
            resolver.Resolve(MapEditorTextureReference.Project("missing.png")).Status);
        Assert.Equal(MapEditorTextureResolutionStatus.LOAD_ERROR,
            resolver.Resolve(broken).Status);
    }

    [Fact]
    public void CachesByLogicalReferenceUntilInvalidated()
    {
        FakeAccess access = new();
        MapEditorTextureReference reference = MapEditorTextureReference.Library(
            "org.example.assets", "terrain/rock.png");
        access.Loads[reference] = Resolved(3);
        MapEditorTextureResolver resolver = new(access);

        resolver.Resolve(reference);
        resolver.Resolve(reference);

        Assert.Equal(1, access.LoadCount);

        resolver.Invalidate();
        resolver.Resolve(reference);

        Assert.Equal(2, access.LoadCount);
    }

    [Fact]
    public void AccessExceptionsBecomeLoadErrors()
    {
        FakeAccess access = new() { Error = new IOException("source unavailable") };
        MapEditorTextureResolver resolver = new(access);

        MapEditorTextureResolution result = resolver.Resolve(
            MapEditorTextureReference.Project("throws.png"));

        Assert.Equal(MapEditorTextureResolutionStatus.LOAD_ERROR, result.Status);
        Assert.Contains("source unavailable", result.Message);
    }

    [Fact]
    public void ContentPackTextureDirectoryIsASharedLibrary()
    {
        string packRoot = Path.Combine(_root, "pack");
        string textureRoot = Path.Combine(packRoot, "textures", "terrain");
        Directory.CreateDirectory(textureRoot);
        string texturePath = Path.Combine(textureRoot, "rock.png");
        using Image image = Image.CreateEmpty(2, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, Colors.Red);
        image.SetPixel(1, 0, Colors.Blue);
        Assert.Equal(Error.Ok, image.SavePng(texturePath));
        ContentPackDefinition pack = new(
            new ContentPackManifest("org.example.pack", "Example", "1"), packRoot);
        MapEditorTextureSourceRegistry registry = new(new FakeAccess());
        registry.RegisterPack(pack);
        MapEditorTextureReference reference = MapEditorTextureReference.Library(
            "org.example.pack", "terrain/rock.png");

        MapEditorTextureResolution result = new MapEditorTextureResolver(registry).Resolve(reference);

        Assert.True(result.IsResolved, result.Message);
        Assert.Equal(2, result.Texture!.Width);
        Assert.Equal(1, result.Texture.Height);
        Assert.Equal("library://org.example.pack/terrain/rock.png", result.ResolvedPath);
    }

    [Fact]
    public void DefaultRegistryIncludesTheSelectedPacksSharedTextureDirectory()
    {
        string packRoot = Path.Combine(_root, "selected-pack");
        Directory.CreateDirectory(Path.Combine(packRoot, "textures"));
        ContentPackDefinition pack = new(
            new ContentPackManifest("org.example.selected", "Selected", "1"), packRoot);
        MapManifest manifest = new() { Name = "Map", SuggestedPlayers = 1 };
        ContentDefinition<MapManifest> definition = new("map", manifest,
            Path.Combine(packRoot, "maps", "map"), Path.Combine(packRoot, "maps", "map", "map.toml"),
            pack);

        MapEditorTextureSourceRegistry registry =
            MapEditorTextureSourceRegistry.CreateDefault(definition);

        Assert.Equal(Path.GetFullPath(Path.Combine(packRoot, "textures")),
            registry.Libraries["org.example.selected"]);
    }

    [Fact]
    public void CatalogListsLibraryTexturesWithPortableReferences()
    {
        string libraryRoot = Path.Combine(_root, "paid");
        string selected = Path.Combine(libraryRoot, "PSX Textures", "wall.png");
        Directory.CreateDirectory(Path.GetDirectoryName(selected)!);
        File.WriteAllBytes(selected, [1, 2, 3]);
        MapEditorTextureSourceRegistry registry = new(new FakeAccess());
        registry.RegisterLibrary("org.mortz.official-source", libraryRoot);

        MapEditorTextureCatalogItem item = Assert.Single(registry.DiscoverTextures(), item =>
            item.Reference.Source == "library:org.mortz.official-source" &&
            item.Reference.Path == "PSX Textures/wall.png");

        Assert.Equal("library:org.mortz.official-source", item.Reference.Source);
        Assert.Equal("PSX Textures/wall.png", item.Reference.Path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(selected));
    }

    [Fact]
    public void CatalogPrefersTheMostSpecificRegisteredLibraryOverProjectUris()
    {
        string libraryRoot = ProjectSettings.GlobalizePath("res://Assets");
        MapEditorTextureSourceRegistry registry = new(new FakeAccess());
        registry.RegisterLibrary("org.example.assets", libraryRoot);

        MapEditorTextureCatalogItem item = Assert.Single(registry.DiscoverTextures(), item =>
            item.Reference.Path == "ScifiCritters4.PNG");

        Assert.Equal("library://org.example.assets/ScifiCritters4.PNG", item.Reference.Location);
    }

    [Fact]
    public void CatalogOnlyExposesProjectTexturesFromTheOfficialTextureDirectory()
    {
        MapEditorTextureSourceRegistry registry = new(new FakeAccess());

        MapEditorTextureCatalogItem[] items = registry.DiscoverTextures()
            .Where(item => item.Reference.Source == MapEditorTextureReference.PROJECT_SOURCE)
            .ToArray();

        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.StartsWith("official/Assets/Textures/",
            item.Reference.Path, StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => item.Reference.Path == "Assets/ScifiCritters4.PNG");
    }

    [Fact]
    public void MissingLibraryIsReportedAsMissing()
    {
        MapEditorTextureSourceRegistry registry = new(new FakeAccess());
        MapEditorTextureReference reference = MapEditorTextureReference.Library(
            "org.example.not-installed", "terrain/rock.png");

        MapEditorTextureResolution result = new MapEditorTextureResolver(registry).Resolve(reference);

        Assert.Equal(MapEditorTextureResolutionStatus.MISSING, result.Status);
        Assert.Contains("isn't available", result.Message);
    }

    private static MapEditorTextureLoadResult Resolved(byte value) => new(
        MapEditorTextureLoadStatus.RESOLVED,
        new MapEditorTextureData(1, 1, [value, value, value, 255]));

    private sealed class FakeAccess : IMapEditorTextureAccess
    {
        public Dictionary<MapEditorTextureReference, MapEditorTextureLoadResult> Loads { get; } = [];
        public Exception? Error { get; init; }
        public int LoadCount { get; private set; }

        public MapEditorTextureLoadResult Load(MapEditorTextureReference reference)
        {
            LoadCount++;
            if (Error != null)
                throw Error;
            return Loads.GetValueOrDefault(reference,
                new MapEditorTextureLoadResult(MapEditorTextureLoadStatus.MISSING));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
