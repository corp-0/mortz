using System.Collections.Immutable;
using Mortz.Content;
using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server;
using Mortz.Server.Content;
using Mortz.Server.Diagnostics;
using Serilog.Core;

namespace Mortz.Tests.Server;

/// <summary>A whole GameServer with no engine and no socket behind it: every
/// byte lands in <see cref="Link"/> and every map comes from <see cref="Maps"/>.</summary>
internal sealed class TestServer : IDisposable
{
    /// <summary>ContentCatalog only loads from a directory, so one empty one is
    /// minted for the process and every server boots from it.</summary>
    private static readonly ContentCatalog _catalog = LoadEmptyCatalog();

    public TestServer(string adminPassword = "", MatchConfig? rules = null,
        IMatchObserver? observer = null, IMatchControl? control = null,
        bool allowJoinInProgress = true)
    {
        Maps.Add(Map("arena", "Arena"));
        Boot = new ServerBoot
        {
            Map = Map("arena", "Arena"),
            Rules = rules ?? new MatchConfig(),
            Catalog = _catalog,
            AdminPassword = adminPassword,
            Name = "test",
            GamePort = 7777,
            QueryPort = 7778,
            Seed = 1,
            NetStats = false,
            AllowJoinInProgress = allowJoinInProgress,
        };
        Server = new GameServer(Boot, Link, Maps, Logger.None,
            observer ?? new NullMatchObserver(), control ?? new NullMatchControl());
    }

    public RecordingTransport Link { get; } = new();

    public FakeMapSource Maps { get; } = new();

    public ServerBoot Boot { get; }

    public GameServer Server { get; }

    public static MapSnapshot Map(string id, string name) => new()
    {
        MapId = id,
        DisplayName = name,
        Hash = $"{id}-hash",
        Width = 64,
        Height = 64,
        SpawnPoints = ImmutableArray.Create(new Vec2(8, 8), new Vec2(48, 8), new Vec2(24, 8)),
        InitialTerrain = new TerrainMask(64, 64, (_, _) => false, (_, _) => false),
    };

    /// <summary>Serializes exactly as a client would, so the router sees real bytes.</summary>
    public void Receive<TMsg>(int peerId, in TMsg message)
        where TMsg : struct, INetMessage<TMsg> =>
        Server.Receive(peerId, TMsg.MsgId, TMsg.Serialize(message));

    public void Connect(int peerId, string name)
    {
        Server.Connect(peerId, name);
        Ready(peerId);
    }

    public void Ready(int peerId)
    {
        int generation = Link.Messages.Select(sent => sent.Message)
            .Where(message => message is PhaseLoadMsg || message is Mortz.Core.Net.Sim.WelcomeMsg)
            .Select(message => message is PhaseLoadMsg lobby
                ? lobby.Generation
                : ((Mortz.Core.Net.Sim.WelcomeMsg)message).Generation)
            .Last();
        Receive(peerId, new PhaseReadyMsg(generation));
    }

    public void Tick(ulong ms = 0)
    {
        int before = Link.Messages.Count;
        Server.Advance(new ServerTime(ms, 1.0 / 60));
        (int PeerId, int Generation)[] loads = [.. Link.Messages.Skip(before)
            .Select(sent => sent.Message switch
            {
                PhaseLoadMsg lobby => (sent.Target, lobby.Generation),
                Mortz.Core.Net.Sim.WelcomeMsg match => (sent.Target, match.Generation),
                _ => (0, 0),
            })
            .Where(load => load.Item1 != 0)];
        foreach ((int peerId, int generation) in loads)
        {
            Receive(peerId, new PhaseReadyMsg(generation));
        }
    }

    public void AdvanceWithoutReady(ulong ms = 0) =>
        Server.Advance(new ServerTime(ms, 1.0 / 60));

    public void Dispose() => Server.Dispose();

    private static ContentCatalog LoadEmptyCatalog()
    {
        string root = Path.Combine(Path.GetTempPath(), "mortz-empty-catalog");
        Directory.CreateDirectory(root);
        return ContentCatalog.Load(root).Catalog ??
               throw new InvalidOperationException("empty content root did not produce a catalog");
    }
}
