namespace Mortz.Content;

public sealed record MapPackageWriteRequest(
    string MapId,
    MapManifest Manifest,
    ReadOnlyMemory<byte> BackgroundPng,
    ReadOnlyMemory<byte> SolidPng,
    ReadOnlyMemory<byte> DestructiblePng);

/// <summary>Writes the whole package off to the side, then swaps the finished
/// directory in and puts the old one back if anything goes wrong. Nothing is
/// edited in place, so a reader can catch the rename and fail, but it can never
/// read half of one map and half of another.</summary>
public static class MapPackageWriter
{
    public static void Write(string mapsDirectory, MapPackageWriteRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        ArgumentNullException.ThrowIfNull(request);
        if (!ContentManifestReader.IsLogicalId(request.MapId))
            throw new ArgumentException("map ID is not a valid logical ID", nameof(request));
        if (request.BackgroundPng.IsEmpty || request.SolidPng.IsEmpty || request.DestructiblePng.IsEmpty)
            throw new ArgumentException("all three PNG layers are required", nameof(request));

        string root = Path.GetFullPath(mapsDirectory);
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
                ContentManifestReader.WriteMap(request.Manifest));

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
