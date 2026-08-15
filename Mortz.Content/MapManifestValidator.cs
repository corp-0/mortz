using Mortz.Core.Match.Teams;
using Mortz.Core.Net;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Content;

public enum ContentValidationMode
{
    REPORT,
    ENFORCE,
}

public readonly record struct MapDimensions(int Width, int Height);

public static class MapManifestValidator
{
    public static IReadOnlyList<ContentDiagnostic> Validate(
        MapManifest manifest,
        string source,
        MapDimensions? dimensions = null,
        ContentValidationMode mode = ContentValidationMode.ENFORCE)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        List<ContentDiagnostic> diagnostics = [];
        ContentDiagnosticSeverity severity = mode == ContentValidationMode.ENFORCE
            ? ContentDiagnosticSeverity.ERROR
            : ContentDiagnosticSeverity.WARNING;

        if (manifest.SuggestedPlayers is < 1 or > NetConfig.MAX_PLAYERS)
        {
            Add($"suggested_players must be between 1 and {NetConfig.MAX_PLAYERS}");
        }

        MapSpawnPoint[] spawnPoints = manifest.SpawnPoints;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            MapSpawnPoint spawn = spawnPoints[i];
            if (spawn.Team is Team team && !Enum.IsDefined(team))
            {
                Add($"spawn_points[{i}].team has invalid value {(byte)team}");
            }
            if (dimensions is MapDimensions size &&
                (spawn.X < 0 || spawn.X >= size.Width || spawn.Y < 0 || spawn.Y >= size.Height))
            {
                Add($"spawn_points[{i}] at ({spawn.X}, {spawn.Y}) is outside " +
                    $"the {size.Width}x{size.Height} map");
            }
        }

        MapZoneDef[] zones = manifest.Zones;
        HashSet<string> zoneNames = new(StringComparer.Ordinal);
        int effectZoneCount = 0;
        for (int i = 0; i < zones.Length; i++)
        {
            MapZoneDef zone = zones[i];

            if (string.IsNullOrEmpty(zone.Name) || !ContentId.IsValid(zone.Name))
            {
                Add($"zones[{i}].name '{zone.Name}' is not a valid logical name");
            }
            else if (!zoneNames.Add(zone.Name))
            {
                Add($"zones[{i}].name duplicates zone name '{zone.Name}'");
            }

            MapZoneEffect[] effects = zone.Effects;
            if (effects.Length > 0)
                effectZoneCount++;
            for (int effectIndex = 0; effectIndex < effects.Length; effectIndex++)
            {
                if (!float.IsFinite(effects[effectIndex].Value))
                {
                    Add($"zones[{i}].effects[{effectIndex}].value must be finite");
                }
            }

            ValidateShape(zone.Shape, i, dimensions, Add);
        }

        if (effectZoneCount > MapZones.MAX_EFFECT_ZONES)
        {
            Add($"map has {effectZoneCount} effect zones; the maximum is " +
                MapZones.MAX_EFFECT_ZONES);
        }

        return diagnostics;

        void Add(string message) => diagnostics.Add(new ContentDiagnostic(severity, source, message));
    }

    private static void ValidateShape(MapZoneShape? shape, int zoneIndex,
        MapDimensions? dimensions, Action<string> add)
    {
        if (shape == null)
        {
            add($"zones[{zoneIndex}].shape is required");
            return;
        }

        Bounds bounds;
        switch (shape)
        {
            case RectMapZoneShape rect:
                if (rect.Width <= 0)
                    add($"zones[{zoneIndex}].shape.width must be positive");
                if (rect.Height <= 0)
                    add($"zones[{zoneIndex}].shape.height must be positive");
                if (!float.IsFinite(rect.Rotation))
                {
                    add($"zones[{zoneIndex}].shape.rotation must be finite");
                    return;
                }
                if (rect.Width <= 0 || rect.Height <= 0)
                    return;
                bounds = RectBounds(rect);
                break;
            case CircleMapZoneShape circle:
                if (circle.Radius <= 0)
                {
                    add($"zones[{zoneIndex}].shape.radius must be positive");
                    return;
                }
                bounds = new Bounds(circle.X - (double)circle.Radius,
                    circle.Y - (double)circle.Radius,
                    circle.X + (double)circle.Radius,
                    circle.Y + (double)circle.Radius);
                break;
            case EllipseMapZoneShape ellipse:
                if (ellipse.RadiusX <= 0)
                    add($"zones[{zoneIndex}].shape.radius_x must be positive");
                if (ellipse.RadiusY <= 0)
                    add($"zones[{zoneIndex}].shape.radius_y must be positive");
                if (!float.IsFinite(ellipse.Rotation))
                {
                    add($"zones[{zoneIndex}].shape.rotation must be finite");
                    return;
                }
                if (ellipse.RadiusX <= 0 || ellipse.RadiusY <= 0)
                    return;
                bounds = EllipseBounds(ellipse);
                break;
            default:
                add($"zones[{zoneIndex}].shape has unsupported type '{shape.GetType().Name}'");
                return;
        }

        if (dimensions is MapDimensions size &&
            (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > size.Width ||
             bounds.Bottom > size.Height))
        {
            add($"zones[{zoneIndex}].shape extends outside the {size.Width}x{size.Height} map");
        }
    }

    private static Bounds RectBounds(RectMapZoneShape rect)
    {
        double radians = rect.Rotation * Math.PI / 180;
        double cosine = Math.Abs(Math.Cos(radians));
        double sine = Math.Abs(Math.Sin(radians));
        double halfWidth = rect.Width / 2d;
        double halfHeight = rect.Height / 2d;
        double extentX = cosine * halfWidth + sine * halfHeight;
        double extentY = sine * halfWidth + cosine * halfHeight;
        double centerX = rect.X + halfWidth;
        double centerY = rect.Y + halfHeight;
        return new Bounds(centerX - extentX, centerY - extentY,
            centerX + extentX, centerY + extentY);
    }

    private static Bounds EllipseBounds(EllipseMapZoneShape ellipse)
    {
        double radians = ellipse.Rotation * Math.PI / 180;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double extentX = Math.Sqrt(
            Square(ellipse.RadiusX * cosine) + Square(ellipse.RadiusY * sine));
        double extentY = Math.Sqrt(
            Square(ellipse.RadiusX * sine) + Square(ellipse.RadiusY * cosine));
        return new Bounds(ellipse.X - extentX, ellipse.Y - extentY,
            ellipse.X + extentX, ellipse.Y + extentY);
    }

    private static double Square(double value) => value * value;

    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom);
}
