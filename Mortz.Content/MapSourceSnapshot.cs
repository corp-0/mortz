using System.Security.Cryptography;
using System.Text;

namespace Mortz.Content;

/// <summary>
/// Every file of a map, read once. Parsing, decoding and hashing all work off
/// these same bytes: if they re-read the files instead, the hash we advertise
/// could describe a map nobody actually loaded.
/// </summary>
public sealed class MapSourceSnapshot
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    private MapSourceSnapshot(MapDefinition definition, MapManifest manifest,
        byte[] background, byte[] solid, byte[] destructible, byte[] manifestBytes)
    {
        Definition = definition;
        Manifest = manifest;
        BackgroundPng = background;
        SolidPng = solid;
        DestructiblePng = destructible;
        ManifestBytes = manifestBytes;
        CompatibilityHash = string.Join(":",
            Hash(background), Hash(solid), Hash(destructible), Hash(manifestBytes));
    }

    public MapDefinition Definition { get; }
    public MapManifest Manifest { get; }
    public ReadOnlyMemory<byte> BackgroundPng { get; }
    public ReadOnlyMemory<byte> SolidPng { get; }
    public ReadOnlyMemory<byte> DestructiblePng { get; }
    public ReadOnlyMemory<byte> ManifestBytes { get; }
    public string CompatibilityHash { get; }

    public static ContentReadResult<MapSourceSnapshot> Read(MapDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        List<ContentDiagnostic> diagnostics = [];
        try
        {
            byte[] background = ReadRequired(definition.DirectoryPath, "background.png");
            byte[] solid = ReadRequired(definition.DirectoryPath, "solid.png");
            byte[] destructible = ReadRequired(definition.DirectoryPath, "destructible.png");
            byte[] manifestBytes = ReadRequired(definition.DirectoryPath, "map.toml");
            string manifestText = _strictUtf8.GetString(manifestBytes);
            if (manifestText.Length > 0 && manifestText[0] == '\uFEFF')
                manifestText = manifestText[1..];

            ContentReadResult<MapManifest> read = ContentManifestReader.ReadMap(
                manifestText, definition.ManifestPath);
            diagnostics.AddRange(read.Diagnostics);
            if (read.Value is not { } manifest)
                return new ContentReadResult<MapSourceSnapshot>(null, diagnostics);

            return new ContentReadResult<MapSourceSnapshot>(
                new MapSourceSnapshot(definition, manifest, background, solid, destructible,
                    manifestBytes), diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                           or DecoderFallbackException)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR,
                definition.DirectoryPath, $"cannot read map package: {exception.Message}"));
            return new ContentReadResult<MapSourceSnapshot>(null, diagnostics);
        }
    }

    private static byte[] ReadRequired(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"required map file is missing: {path}", path);
        return File.ReadAllBytes(path);
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
