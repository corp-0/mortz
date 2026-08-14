namespace Mortz.Server.Services;

/// <summary>A server frame passed.</summary>
public interface IAdvance
{
    void Advance(ServerTime time);
}
