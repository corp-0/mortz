namespace Mortz.Core;

public readonly record struct SpawnPointValidationError(int Index, Vec2 Position, string Reason);

/// <summary>Checks authored spawn points: a body must fit there and stand on
/// something. Used both when loading a map and when authoring one.</summary>
public static class SpawnPointValidator
{
    public static IReadOnlyList<SpawnPointValidationError> Validate(
        TerrainMask terrain, IReadOnlyList<Vec2> spawnPoints)
    {
        List<SpawnPointValidationError> errors = [];
        Dictionary<Vec2, int> firstIndexByPoint = new();
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Vec2 point = spawnPoints[i];
            if (firstIndexByPoint.TryGetValue(point, out int firstIndex))
                errors.Add(new SpawnPointValidationError(i, point,
                    $"duplicates spawn_points[{firstIndex}]"));
            else if (!BodyInBounds(terrain, point))
                errors.Add(new SpawnPointValidationError(i, point, "player body is out of bounds"));
            else if (PlayerSim.BodyBlocked(terrain, point))
                errors.Add(new SpawnPointValidationError(i, point, "player body overlaps terrain"));
            else if (!PlayerSim.OnGround(terrain, point))
                errors.Add(new SpawnPointValidationError(i, point, "feet are not supported by terrain"));
            firstIndexByPoint.TryAdd(point, i);
        }
        return errors;
    }

    private static bool BodyInBounds(TerrainMask terrain, Vec2 feet) =>
        feet.X - SimConfig.PLAYER_HALF_WIDTH >= 0 &&
        feet.X + SimConfig.PLAYER_HALF_WIDTH <= terrain.Width &&
        feet.Y - SimConfig.PLAYER_HALF_HEIGHT * 2 >= 0 &&
        feet.Y >= 0 && feet.Y < terrain.Height;
}
