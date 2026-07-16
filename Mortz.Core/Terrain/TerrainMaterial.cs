namespace Mortz.Core.Terrain;

public enum TerrainMaterial : byte
{
    Empty = 0,
    Solid = 1,        // collides, indestructible
    Destructible = 2, // collides, carvable
}
