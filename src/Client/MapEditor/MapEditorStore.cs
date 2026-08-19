using System.Collections.Immutable;
using Godot;
using Mortz.Content;
using Mortz.Shared;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorStoredMap(
    ContentDefinition<MapManifest> Definition,
    MapManifest Manifest,
    MapEditorLayers Layers);

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

    MapEditorStoreResult<MapEditorLayerAsset> LoadLayer(
        string? path,
        int expectedWidth,
        int expectedHeight);

    MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
        ContentDefinition<MapManifest> definition,
        MapManifest manifest,
        MapEditorLayers layers,
        int width,
        int height);
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
            new MapEditorLayerAsset(source.BackgroundPng.Span, package.Width, package.Height),
            new MapEditorLayerAsset(source.SolidPng.Span, package.Width, package.Height),
            new MapEditorLayerAsset(source.DestructiblePng.Span, package.Width, package.Height));
        ContentDefinition<MapManifest> loadedDefinition = definition with
        {
            Manifest = source.Manifest,
        };
        return MapEditorStoreResult<MapEditorStoredMap>.Success(
            new MapEditorStoredMap(loadedDefinition, source.Manifest, layers));
    }

    public MapEditorStoreResult<MapEditorLayerAsset> LoadLayer(
        string? path,
        int expectedWidth,
        int expectedHeight)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MapEditorStoreResult<MapEditorLayerAsset>.Failed(
                new MapEditorIoFailure("Replacement image path is required."));
        }

        try
        {
            byte[] png = File.ReadAllBytes(path);
            Image image = new();
            Error error = image.LoadPngFromBuffer(png);
            if (error != Error.Ok)
            {
                return MapEditorStoreResult<MapEditorLayerAsset>.Failed(
                    new MapEditorInvalidPngFailure(path, error.ToString()));
            }

            int actualWidth = image.GetWidth();
            int actualHeight = image.GetHeight();
            if (actualWidth != expectedWidth || actualHeight != expectedHeight)
            {
                return MapEditorStoreResult<MapEditorLayerAsset>.Failed(
                    new MapEditorLayerSizeFailure(
                        expectedWidth, expectedHeight, actualWidth, actualHeight));
            }

            return MapEditorStoreResult<MapEditorLayerAsset>.Success(
                new MapEditorLayerAsset(png, actualWidth, actualHeight));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException)
        {
            return MapEditorStoreResult<MapEditorLayerAsset>.Failed(
                new MapEditorIoFailure(exception.Message));
        }
    }

    public MapEditorStoreResult<ContentDefinition<MapManifest>> Save(
        ContentDefinition<MapManifest> definition,
        MapManifest manifest,
        MapEditorLayers layers,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(layers);

        try
        {
            string mapsDirectory = Directory.GetParent(definition.DirectoryPath)?.FullName ??
                throw new ArgumentException("Map directory has no parent.", nameof(definition));
            MapPackageWriter.Write(mapsDirectory, new MapPackageWriteRequest(
                definition.Id,
                manifest,
                layers.Background.Png,
                layers.Solid.Png,
                layers.Destructible.Png,
                width,
                height));
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
}
