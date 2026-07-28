using Godot;
using Mortz.Content;

namespace Mortz.Shared;

/// <summary>The one way in to content. The server loads one instance at boot
/// so every option comes from the same snapshot; the client loads fresh each
/// time so content installed while the game runs shows up.</summary>
public sealed class GameContent
{
    private GameContent(ContentCatalog catalog) => Catalog = catalog;

    public ContentCatalog Catalog { get; }

    public static GameContent? Load() => Load(ContentRoot.Resolve());

    /// <summary>Null when the catalog is unusable; diagnostics print either way.</summary>
    public static GameContent? Load(string contentRoot)
    {
        ContentCatalogResult result = ContentCatalog.Load(contentRoot);
        Print(result.Diagnostics);
        return result.Catalog == null ? null : new GameContent(result.Catalog);
    }

    public MapPackage? LoadMap(string mapId)
    {
        if (!Catalog.TryGetMap(mapId, out ResolvedContent<MapManifest>? resolved) || resolved == null)
        {
            GD.PrintErr($"[content] logical map '{mapId}' was not found");
            return null;
        }
        MapPackageLoadResult result = MapPackageLoader.Load(resolved.Winner);
        Print(result.Diagnostics);
        return result.Package;
    }

    private static void Print(IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                GD.PrintErr($"[content] {diagnostic}");
            else
                GD.PushWarning($"[content] {diagnostic}");
        }
    }
}
