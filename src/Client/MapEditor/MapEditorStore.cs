using System.Collections.Immutable;
using System.Text.Json;
using Mortz.Content;
using Mortz.Shared;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorStoredMap(
    ContentDefinition<MapManifest> Definition,
    MapManifest Manifest,
    MapEditorLayers Layers,
    MapEditorBrushDocument? BrushDocument = null);

public sealed record MapEditorStoreResult<T>(T? Value, MapEditorOperationFailure? Failure)
    where T : class
{
    public bool Succeeded => Value != null;

    public static MapEditorStoreResult<T> Success(T value) => new(value, null);

    public static MapEditorStoreResult<T> Failed(MapEditorOperationFailure failure) =>
        new(null, failure);
}

public interface IMapEditorStore
{
    MapEditorStoreResult<MapEditorStoredMap> Load(ContentDefinition<MapManifest> definition);

    MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
        ContentDefinition<MapManifest> definition,
        MapManifest manifest,
        MapEditorLayers layers,
        int width,
        int height,
        MapEditorBrushDocument? brushDocument);
}

public sealed class FileMapEditorStore : IMapEditorStore
{
    public MapEditorStoreResult<MapEditorStoredMap> Load(
        ContentDefinition<MapManifest> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        MapPackageLoadResult result = MapPackageLoader.Load(definition);
        if (result.Package == null || result.Source == null)
        {
            return MapEditorStoreResult<MapEditorStoredMap>.Failed(
                new MapEditorContentFailure(result.Diagnostics.ToImmutableArray()));
        }

        MapPackage package = result.Package;
        MapSourceSnapshot source = result.Source;
        MapEditorLayers layers = new(
            MapEditorLayerAsset.AdoptOwnedPng(source.BackgroundPng,
                package.Width, package.Height),
            MapEditorLayerAsset.AdoptOwnedPng(source.SolidPng,
                package.Width, package.Height),
            MapEditorLayerAsset.AdoptOwnedPng(source.DestructiblePng,
                package.Width, package.Height));
        ContentDefinition<MapManifest> loadedDefinition = definition with
        {
            Manifest = source.Manifest,
        };
        string editorPath = Path.Combine(definition.DirectoryPath, "editor.json");
        MapEditorBrushDocument? brushDocument = null;
        if (File.Exists(editorPath))
        {
            try
            {
                brushDocument = MapEditorDocumentJson.Deserialize(
                    File.ReadAllBytes(editorPath), layers);
                ImmutableArray<ContentDiagnostic> editorDiagnostics = MapEditorBrushValidator.Validate(
                    brushDocument, editorPath, package.Width, package.Height);
                if (editorDiagnostics.Any(diagnostic =>
                        diagnostic.Severity == ContentDiagnosticSeverity.ERROR))
                {
                    return MapEditorStoreResult<MapEditorStoredMap>.Failed(
                        new MapEditorContentFailure(editorDiagnostics));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or JsonException or MapEditorDocumentVersionException
                                                  or InvalidOperationException or FormatException
                                                  or OverflowException)
            {
                return MapEditorStoreResult<MapEditorStoredMap>.Failed(
                    new MapEditorContentFailure([
                        new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, editorPath,
                            exception.Message),
                    ]));
            }
        }

        return MapEditorStoreResult<MapEditorStoredMap>.Success(
            new MapEditorStoredMap(loadedDefinition, source.Manifest, layers, brushDocument));
    }

    public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
        ContentDefinition<MapManifest> definition,
        MapManifest manifest,
        MapEditorLayers layers,
        int width,
        int height,
        MapEditorBrushDocument? brushDocument)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(layers);

        try
        {
            string editorPath = Path.Combine(definition.DirectoryPath, "editor.json");
            EnsureExistingEditorCanBeReplaced(editorPath, layers, brushDocument != null);
            Dictionary<string, ReadOnlyMemory<byte>>? additionalFiles = null;
            if (brushDocument != null)
            {
                ImmutableArray<ContentDiagnostic> diagnostics = MapEditorBrushValidator.Validate(
                    brushDocument, editorPath,
                    width, height);
                if (diagnostics.Any(diagnostic =>
                        diagnostic.Severity == ContentDiagnosticSeverity.ERROR))
                    throw new ContentValidationException(diagnostics);
                additionalFiles = new Dictionary<string, ReadOnlyMemory<byte>>
                {
                    ["editor.json"] = MapEditorDocumentJson.Serialize(brushDocument),
                };
            }

            string mapsDirectory = Directory.GetParent(definition.DirectoryPath)?.FullName ??
                                   throw new ArgumentException("Map directory has no parent.", nameof(definition));
            MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
                definition.Id,
                manifest,
                layers.Background.Png,
                layers.Solid.Png,
                layers.Destructible.Png,
                width,
                height,
                additionalFiles));
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Success(
                definition with { Manifest = manifest });
        }
        catch (ContentValidationException exception)
        {
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Failed(
                new MapEditorContentFailure(exception.Diagnostics.ToImmutableArray()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException)
        {
            return MapEditorStoreResult<ContentDefinition<MapManifest>>.Failed(
                new MapEditorIoFailure(exception.Message));
        }
    }

    private static void EnsureExistingEditorCanBeReplaced(string editorPath,
        MapEditorLayers layers,
        bool hasReplacement)
    {
        if (!File.Exists(editorPath))
            return;
        if (!hasReplacement)
        {
            throw EditorValidationFailure(editorPath,
                "This map's editing data can't be removed by saving layer images only.");
        }

        try
        {
            MapEditorDocumentJson.Deserialize(File.ReadAllBytes(editorPath), layers);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or MapEditorDocumentVersionException
                                              or InvalidOperationException or FormatException
                                              or OverflowException)
        {
            throw EditorValidationFailure(editorPath,
                $"The map's editing data can't be replaced: {exception.Message}");
        }
    }

    private static ContentValidationException EditorValidationFailure(string source,
        string message) => new([
        new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message),
    ]);
}
