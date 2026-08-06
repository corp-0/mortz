using Mortz.Server.Content;
using Mortz.Shared;

namespace Mortz.Server.Pump;

/// <summary>IMapSource over the Godot content pipeline: decode here, snapshot out.</summary>
public sealed class GodotMapSource(GameContent content) : IMapSource
{
    public MapSnapshot? Load(string mapId) => content.LoadMap(mapId)?.ToSnapshot();
}
