using System.Collections.Immutable;
using System.Text.Json;

namespace Mortz.Client.MapEditor;

public sealed class MapEditorDocumentVersionException(int version) : Exception(
    $"editor.json version {version} is newer than the supported version {MapEditorBrushDocument.CURRENT_VERSION}.")
{
    public int Version { get; } = version;
}

public static class MapEditorDocumentJson
{
    public static byte[] Serialize(MapEditorBrushDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", MapEditorBrushDocument.CURRENT_VERSION);
            writer.WriteNumber("nextBrushId", document.NextBrushId);
            writer.WriteNumber("nextStampId", document.NextStampId);
            WritePoint(writer, "origin", document.Origin);
            writer.WritePropertyName("layers");
            writer.WriteStartArray();
            foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
            {
                WriteLayer(writer, layer, document.Layers.Get(layer));
            }

            writer.WriteEndArray();
            writer.WritePropertyName("stamps");
            writer.WriteStartArray();
            foreach (MapEditorStamp stamp in document.Stamps.IsDefault ? [] : document.Stamps)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", stamp.Id.Value);
                writer.WriteString("name", stamp.Name);
                writer.WriteString("layer", stamp.Brush.Layer.ToString());
                WriteBrushValue(writer, stamp.Brush);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    public static MapEditorBrushDocument Deserialize(ReadOnlySpan<byte> json,
        MapEditorLayers bakedLayers)
    {
        ArgumentNullException.ThrowIfNull(bakedLayers);
        using JsonDocument parsed = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        JsonElement root = parsed.RootElement;
        int version = Required(root, "version").GetInt32();
        if (version > MapEditorBrushDocument.CURRENT_VERSION)
            throw new MapEditorDocumentVersionException(version);
        if (version is < 1 or > MapEditorBrushDocument.CURRENT_VERSION)
            throw new JsonException($"Unsupported editor.json version {version}.");

        long nextBrushId = Required(root, "nextBrushId").GetInt64();
        MapEditorMapOrigin origin = root.TryGetProperty("origin", out JsonElement originElement)
            ? ReadOrigin(originElement)
            : default;
        Dictionary<MapEditorLayer, MapEditorLayerSource> layers = [];
        foreach (JsonElement layerElement in Required(root, "layers").EnumerateArray())
        {
            MapEditorLayer layer = ParseEnum<MapEditorLayer>(Required(layerElement, "layer"));
            if (!layers.TryAdd(layer, ReadLayer(layerElement, layer,
                    GetBaked(bakedLayers, layer))))
                throw new JsonException($"editor.json contains duplicate {layer} layers.");
        }

        foreach (MapEditorLayer layer in Enum.GetValues<MapEditorLayer>())
        {
            if (!layers.ContainsKey(layer))
                throw new JsonException($"editor.json is missing the {layer} layer.");
        }

        long nextStampId = version >= 2
            ? Required(root, "nextStampId").GetInt64()
            : 1;
        ImmutableArray<MapEditorStamp> stamps = version >= 2
            ? Required(root, "stamps").EnumerateArray().Select(ReadStamp).ToImmutableArray()
            : [];
        return new MapEditorBrushDocument(MapEditorBrushDocument.CURRENT_VERSION, nextBrushId,
            new MapEditorLayerSources(layers[MapEditorLayer.BACKGROUND],
                layers[MapEditorLayer.SOLID], layers[MapEditorLayer.DESTRUCTIBLE]), origin,
            nextStampId, stamps);
    }

    private static void WriteLayer(Utf8JsonWriter writer, MapEditorLayer layer,
        MapEditorLayerSource source)
    {
        writer.WriteStartObject();
        writer.WriteString("layer", layer.ToString());
        writer.WritePropertyName("brushes");
        writer.WriteStartArray();
        foreach (MapEditorBrush brush in source.Brushes)
        {
            WriteBrush(writer, brush);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteBrush(Utf8JsonWriter writer, MapEditorBrush brush)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", brush.Id.Value);
        writer.WriteString("name", brush.Name);
        WriteBrushValue(writer, new MapEditorBrushDraft(brush.Name, brush.Layer, brush.Shape,
            brush.Material, brush.Projection, brush.Visible));
        writer.WriteEndObject();
    }

    private static void WriteBrushValue(Utf8JsonWriter writer, MapEditorBrushDraft brush)
    {
        writer.WritePropertyName("shape");
        WriteShape(writer, brush.Shape);
        writer.WritePropertyName("material");
        writer.WriteStartObject();
        switch (brush.Material)
        {
            case MapEditorTextureMaterial texture:
                writer.WriteString("kind", "TEXTURE");
                writer.WriteString("source", texture.Reference.Source);
                writer.WriteString("path", texture.Reference.Path);
                break;
            case MapEditorSolidColorMaterial solid:
                writer.WriteString("kind", "COLOR");
                writer.WritePropertyName("rgba");
                writer.WriteStartArray();
                writer.WriteNumberValue(solid.Color.Red);
                writer.WriteNumberValue(solid.Color.Green);
                writer.WriteNumberValue(solid.Color.Blue);
                writer.WriteNumberValue(solid.Color.Alpha);
                writer.WriteEndArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(brush.Material));
        }
        writer.WriteEndObject();
        writer.WritePropertyName("projection");
        writer.WriteStartObject();
        writer.WriteString("mode", brush.Projection.Mode.ToString());
        WritePoint(writer, "origin", brush.Projection.Origin);
        writer.WritePropertyName("scale");
        writer.WriteStartArray();
        writer.WriteNumberValue(brush.Projection.ScaleX);
        writer.WriteNumberValue(brush.Projection.ScaleY);
        writer.WriteEndArray();
        writer.WriteNumber("rotation", brush.Projection.Rotation);
        writer.WriteEndObject();
        writer.WriteBoolean("visible", brush.Visible);
    }

    private static MapEditorStamp ReadStamp(JsonElement element)
    {
        string name = Required(element, "name").GetString() ??
                      throw new JsonException("stamp name is null.");
        MapEditorLayer layer = ParseEnum<MapEditorLayer>(Required(element, "layer"));
        return new MapEditorStamp(
            new MapEditorStampId(Required(element, "id").GetInt64()), name,
            ReadBrushValue(element, name, layer));
    }

    private static void WriteShape(Utf8JsonWriter writer, MapEditorBrushShape shape)
    {
        writer.WriteStartObject();
        switch (shape)
        {
            case MapEditorRectBrushShape rect:
                writer.WriteString("kind", "RECTANGLE");
                writer.WriteNumber("x", rect.X);
                writer.WriteNumber("y", rect.Y);
                writer.WriteNumber("width", rect.Width);
                writer.WriteNumber("height", rect.Height);
                writer.WriteNumber("rotation", rect.Rotation);
                break;
            case MapEditorEllipseBrushShape ellipse:
                writer.WriteString("kind", "ELLIPSE");
                writer.WriteNumber("x", ellipse.X);
                writer.WriteNumber("y", ellipse.Y);
                writer.WriteNumber("radiusX", ellipse.RadiusX);
                writer.WriteNumber("radiusY", ellipse.RadiusY);
                writer.WriteNumber("rotation", ellipse.Rotation);
                break;
            case MapEditorPolygonBrushShape polygon:
                writer.WriteString("kind", "POLYGON");
                writer.WritePropertyName("vertices");
                writer.WriteStartArray();
                foreach (MapEditorPoint point in polygon.Vertices)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(point.X);
                    writer.WriteNumberValue(point.Y);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        writer.WriteEndObject();
    }

    private static MapEditorLayerSource ReadLayer(JsonElement element, MapEditorLayer layer,
        MapEditorLayerAsset baked)
    {
        ImmutableArray<MapEditorBrush>.Builder brushes = ImmutableArray.CreateBuilder<MapEditorBrush>();
        foreach (JsonElement brush in Required(element, "brushes").EnumerateArray())
        {
            brushes.Add(ReadBrush(brush, layer));
        }

        return new MapEditorLayerSource(brushes.ToImmutable(), baked, false);
    }

    private static MapEditorBrush ReadBrush(JsonElement element, MapEditorLayer layer)
    {
        string name = Required(element, "name").GetString() ??
                      throw new JsonException("brush name is null.");
        MapEditorBrushDraft value = ReadBrushValue(element, name, layer);
        return new MapEditorBrush(
            new MapEditorBrushId(Required(element, "id").GetInt64()), name, layer,
            value.Shape, value.Material, value.Projection, value.Visible);
    }

    private static MapEditorBrushDraft ReadBrushValue(JsonElement element, string name,
        MapEditorLayer layer)
    {
        JsonElement material = Required(element, "material");
        JsonElement projection = Required(element, "projection");
        float[] scale = Required(projection, "scale").EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (scale.Length != 2)
            throw new JsonException("projection scale must contain two values.");
        return new MapEditorBrushDraft(name, layer,
            ReadShape(Required(element, "shape")),
            ReadMaterial(material),
            new MapEditorTextureProjection(
                ParseEnum<MapEditorProjectionMode>(Required(projection, "mode")),
                ReadPoint(Required(projection, "origin")), scale[0], scale[1],
                Required(projection, "rotation").GetSingle()),
            Required(element, "visible").GetBoolean());
    }

    private static MapEditorBrushMaterial ReadMaterial(JsonElement element)
    {
        string kind = Required(element, "kind").GetString() ??
                      throw new JsonException("material kind is null.");
        if (kind == "TEXTURE")
        {
            return new MapEditorTextureMaterial(new MapEditorTextureReference(
                Required(element, "source").GetString() ??
                throw new JsonException("texture source is null."),
                Required(element, "path").GetString() ??
                throw new JsonException("texture path is null.")));
        }
        if (kind == "COLOR")
        {
            byte[] rgba = Required(element, "rgba").EnumerateArray()
                .Select(value => value.GetByte()).ToArray();
            if (rgba.Length != 4)
                throw new JsonException("material rgba must contain four bytes.");
            return new MapEditorSolidColorMaterial(
                new MapEditorColor(rgba[0], rgba[1], rgba[2], rgba[3]));
        }
        throw new JsonException($"Unknown material kind '{kind}'.");
    }

    private static MapEditorBrushShape ReadShape(JsonElement element)
    {
        string kind = Required(element, "kind").GetString() ??
                      throw new JsonException("shape kind is null.");
        return kind switch
        {
            "RECTANGLE" => new MapEditorRectBrushShape(Required(element, "x").GetInt32(),
                Required(element, "y").GetInt32(), Required(element, "width").GetInt32(),
                Required(element, "height").GetInt32(), Required(element, "rotation").GetSingle()),
            "ELLIPSE" => new MapEditorEllipseBrushShape(Required(element, "x").GetInt32(),
                Required(element, "y").GetInt32(), Required(element, "radiusX").GetInt32(),
                Required(element, "radiusY").GetInt32(), Required(element, "rotation").GetSingle()),
            "POLYGON" => new MapEditorPolygonBrushShape(Required(element, "vertices")
                .EnumerateArray().Select(ReadPoint).ToImmutableArray()),
            _ => throw new JsonException($"Unknown brush shape kind '{kind}'."),
        };
    }

    private static void WritePoint(Utf8JsonWriter writer, string name, MapEditorPoint point)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteNumberValue(point.X);
        writer.WriteNumberValue(point.Y);
        writer.WriteEndArray();
    }

    private static void WritePoint(Utf8JsonWriter writer, string name, MapEditorMapOrigin point)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteNumberValue(point.X);
        writer.WriteNumberValue(point.Y);
        writer.WriteEndArray();
    }

    private static MapEditorPoint ReadPoint(JsonElement element)
    {
        int[] coordinates = element.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (coordinates.Length != 2)
            throw new JsonException("point must contain two integers.");
        return new MapEditorPoint(coordinates[0], coordinates[1]);
    }

    private static MapEditorMapOrigin ReadOrigin(JsonElement element)
    {
        long[] coordinates = element.EnumerateArray().Select(value => value.GetInt64()).ToArray();
        if (coordinates.Length != 2)
            throw new JsonException("origin must contain two integers.");
        return new MapEditorMapOrigin(coordinates[0], coordinates[1]);
    }

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new JsonException($"Required property '{name}' is missing.");

    private static T ParseEnum<T>(JsonElement element) where T : struct, Enum
    {
        string value = element.GetString() ?? throw new JsonException("Enum value is null.");
        return Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new JsonException($"Unknown {typeof(T).Name} value '{value}'.");
    }

    private static MapEditorLayerAsset GetBaked(MapEditorLayers layers, MapEditorLayer layer) =>
        layer switch
        {
            MapEditorLayer.BACKGROUND => layers.Background,
            MapEditorLayer.SOLID => layers.Solid,
            MapEditorLayer.DESTRUCTIBLE => layers.Destructible,
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
}
