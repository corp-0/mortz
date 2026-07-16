namespace Mortz.Core.Terrain;

public enum TerrainMaterial : byte
{
    EMPTY = 0,
    SOLID = 1,        // collides, indestructible
    DESTRUCTIBLE = 2, // collides, carvable
}
