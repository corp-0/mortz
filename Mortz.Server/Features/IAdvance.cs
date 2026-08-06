namespace Mortz.Server.Features;

/// <summary>A frame passed.</summary>
public interface IAdvance
{
    void Advance(ServerTime time);
}
