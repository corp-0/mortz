using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net;

namespace Mortz.Client.Session;

public class ClientConnectionScope : IDisposable
{
    public MatchSetup Setup { get; }
    public ClientAdmin Admin { get; }
    public ClientChat Chat { get; }
    private readonly NetRouter _router;
    public ClientMatchRuntime? Match { get; private set; }
    public FeatureScope Features { get; }
    public ClientPlayers Players { get; }
    public Pings Pings { get; }
    public SessionWins Wins { get; }

    public ClientConnectionScope(NetRouter router, IClientSender sender, Func<int> localPeerId, ISessionExit sessionExit)
    {
        _router = router;
        Features = new FeatureScope(router.Add, router.Remove);
        Players = Features.Register(new ClientPlayers());
        Pings = Features.Register(new Pings(Players));
        Wins = Features.Register(new SessionWins(Players));
        Setup = Features.Register(new MatchSetup());
        Admin = Features.Register(new ClientAdmin(sender, localPeerId));
        Chat = Features.Register(new ClientChat(Admin, sessionExit, sender));
        Players.SessionKeys.Seal();
        Features.OwnState(Players.Dispose, Players.SessionKeys.Describe);
        Features.Own(CloseMatch);
        Features.Start();
    }

    public ClientMatchRuntime OpenMatch(ClientMatchState state, TerrainMask terrain,
        MatchConfig config, MapZones zones, int localPeerId, Action<byte[]> sendInputs, Func<ulong> clock)
    {
        if (Features.Lifetime.IsCancellationRequested)
            throw new ObjectDisposedException(nameof(ClientConnectionScope));
        CloseMatch();
        _router.MatchGeneration = state.Generation;
        Match = new ClientMatchRuntime(state, Players, terrain, config, zones, localPeerId,
            _router, sendInputs, clock);
        return Match;
    }

    public void CloseMatch()
    {
        Match?.Dispose();
        Match = null;
        _router.MatchGeneration = -1;
    }

    public string Describe() => Features.Describe();

    public void Dispose() => Features.Dispose();
}
