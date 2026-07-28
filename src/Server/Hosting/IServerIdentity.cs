namespace Mortz.Server.Hosting;

/// <summary>What a server tells browsers about itself.</summary>
public interface IServerIdentity
{
    string Name { get; }
    int GamePort { get; }
    int QueryPort { get; }
}
