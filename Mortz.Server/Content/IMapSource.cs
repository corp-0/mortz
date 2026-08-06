namespace Mortz.Server.Content;

/// <summary>The engine seam for map bytes: PNG decode happens behind it, on the Godot side.</summary>
public interface IMapSource
{
    MapSnapshot? Load(string mapId);
}
