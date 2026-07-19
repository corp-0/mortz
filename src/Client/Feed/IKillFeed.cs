namespace Mortz.Client.Feed;

/// <summary>Stream of display lines for eliminations and match results.</summary>
public interface IKillFeed
{
    event Action<string>? LineAdded;
}
