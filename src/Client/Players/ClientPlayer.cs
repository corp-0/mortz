using Mortz.Core.Match.Teams;
using Mortz.Core.Net.Lobby;
using Mortz.Core.Net.Roster;
using Mortz.Core.Sim;

namespace Mortz.Client.Players;

/// <summary>One player this client has heard of, from any stream.</summary>
public sealed class ClientPlayer(
    int peerId,
    SessionStateKeys sessionKeys,
    PlayerStats? matchBaseStats)
{
    private object?[]? _session;

    public int PeerId { get; } = peerId;

    public ClientMatchPlayer? Match { get; private set; } =
        matchBaseStats == null ? null : new ClientMatchPlayer(matchBaseStats);

    private readonly record struct Identity(string Name, Team? Team, byte Skin, bool Ready);

    private Identity _identity = new(UnknownName(peerId), null, 0, false);

    /// <summary>Placeholder until the first broadcast lands, deliberately
    /// unlike a real name so a lookup race cannot pass for one.</summary>
    public string Name => _identity.Name;

    /// <summary>Null when teams are off or no broadcast assigned one yet.</summary>
    public Team? Team => _identity.Team;

    public byte Skin => _identity.Skin;

    public bool Ready => _identity.Ready;

    public bool IsTyping { get; private set; }

    /// <summary>False between first reference by a faster stream and the
    /// broadcast that names this player.</summary>
    public bool Known { get; private set; }

    /// <summary>Identity changed: first confirmation, rename, team move,
    /// ready toggle, skin change.</summary>
    public event Action? Changed;

    public T State<T>(SessionStateKey<T> key) where T : class, new()
    {
        if (key.Generation != sessionKeys.Generation)
            throw new InvalidOperationException($"Stale {typeof(T).Name} state key.");
        return Cell<T>(ref _session, sessionKeys.Count, key.Index);
    }

    public bool Apply(LobbyMember member) => Confirm(_identity with
    {
        Name = member.Name,
        Team = member.Team,
        Ready = member.Ready,
    });

    public bool Apply(RosterEntry entry) => Confirm(_identity with
    {
        Name = entry.Name,
        Team = entry.Team,
        Skin = entry.Skin,
    });

    internal void ApplyTyping(bool isTyping) => IsTyping = isTyping;

    private bool Confirm(Identity next)
    {
        bool changed = !Known || next != _identity;
        Known = true;
        _identity = next;
        if (changed)
            Changed?.Invoke();
        return changed;
    }

    public void OpenMatch(PlayerStats baseStats)
    {
        Match = new ClientMatchPlayer(baseStats);
    }

    public void CloseMatch()
    {
        Match = null;
    }

    /// <summary>Left the server: match cells, then session cells, reverse
    /// claim order within each.</summary>
    public void Retire()
    {
        CloseMatch();
        DisposeReverse(_session);
        _session = null;
    }

    // Unlike the server, features claim keys as scenes mount, so a player can
    // need a cell before the last claim happened. Size to the mint count on
    // first touch and grow when a later claim overtook it.
    private static T Cell<T>(ref object?[]? cells, int count, int index) where T : class, new()
    {
        cells ??= new object?[count];
        if (index >= cells.Length)
            Array.Resize(ref cells, count);
        return (T)(cells[index] ??= new T());
    }

    public static string UnknownName(int peerId) => $"<unknown {peerId}>";

    private static void DisposeReverse(object?[]? cells)
    {
        if (cells == null)
            return;
        for (int i = cells.Length - 1; i >= 0; i--)
        {
            if (cells[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
