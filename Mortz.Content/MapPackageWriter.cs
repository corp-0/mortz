namespace Mortz.Content;

public sealed record MapPackageWriteRequest(
    string MapId,
    MapManifest Manifest,
    ReadOnlyMemory<byte> BackgroundPng,
    ReadOnlyMemory<byte> SolidPng,
    ReadOnlyMemory<byte> DestructiblePng,
    int? ImageWidth = null,
    int? ImageHeight = null);

/// <summary>Replaces a map package without exposing a half-written one.</summary>
public static class MapPackageWriter
{
    public static void Write(string mapsDirectory, MapPackageWriteRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        ArgumentNullException.ThrowIfNull(request);
        if (!ContentId.IsValid(request.MapId))
            throw new ArgumentException("map ID is not a valid logical ID", nameof(request));
        if (request.BackgroundPng.IsEmpty || request.SolidPng.IsEmpty || request.DestructiblePng.IsEmpty)
            throw new ArgumentException("all three PNG layers are required", nameof(request));
        if (request.ImageWidth.HasValue != request.ImageHeight.HasValue ||
            request.ImageWidth is <= 0 || request.ImageHeight is <= 0)
        {
            throw new ArgumentException(
                "image width and height must both be omitted or positive", nameof(request));
        }

        string root = Path.GetFullPath(mapsDirectory);
        string manifestPath = Path.Combine(root, request.MapId, "map.toml");
        MapDimensions? dimensions = request.ImageWidth is int width &&
                                    request.ImageHeight is int height
            ? new MapDimensions(width, height)
            : null;
        IReadOnlyList<ContentDiagnostic> diagnostics = MapManifestValidator.Validate(
            request.Manifest, manifestPath, dimensions);
        if (diagnostics.Any(diagnostic =>
                diagnostic.Severity == ContentDiagnosticSeverity.ERROR))
        {
            throw new ContentValidationException(diagnostics);
        }

        Directory.CreateDirectory(root);
        string target = Path.Combine(root, request.MapId);
        string transaction = Guid.NewGuid().ToString("N");
        string transactionRoot = Path.Combine(Directory.GetParent(root)!.FullName,
            ".mortz-transactions");
        Directory.CreateDirectory(transactionRoot);
        string staging = Path.Combine(transactionRoot, $"{request.MapId}.staging-{transaction}");
        string backup = Path.Combine(transactionRoot, $"{request.MapId}.backup-{transaction}");
        bool targetMoved = false;
        bool committed = false;
        try
        {
            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, "background.png"), request.BackgroundPng.ToArray());
            File.WriteAllBytes(Path.Combine(staging, "solid.png"), request.SolidPng.ToArray());
            File.WriteAllBytes(Path.Combine(staging, "destructible.png"), request.DestructiblePng.ToArray());
            File.WriteAllText(Path.Combine(staging, "map.toml"),
                TomlModel.Write(request.Manifest));

            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                targetMoved = true;
            }
            Directory.Move(staging, target);
            committed = true;
            if (targetMoved)
                TryDeleteTree(backup);
        }
        catch
        {
            if (!committed && targetMoved && !Directory.Exists(target) && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
                TryDeleteTree(staging);
            if (committed && Directory.Exists(backup))
                TryDeleteTree(backup);
            TryDeleteEmpty(transactionRoot);
        }
    }

    private static void TryDeleteTree(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Leftover junk is not worth failing a write that already landed.
        }
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another writer may have moved in since the emptiness check.
        }
    }
}
