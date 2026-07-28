namespace Mortz.Server;

/// <summary>What a server tells browsers about itself.</summary>
public interface IServerIdentity
{
    string Name { get; }
    int GamePort { get; }
    int QueryPort { get; }
}
