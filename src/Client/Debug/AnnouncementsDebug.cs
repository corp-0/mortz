using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Announcements;
using Mortz.Client.Audio;
using Mortz.Client.Chat;
using Mortz.Client.Roster;
using Mortz.Client.Session;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Debug;

/// <summary>Run isolated (F6) to fire announcements without a live match.</summary>
[Meta(typeof(IAutoNode))]
public partial class AnnouncementsDebug : Control,
    IProvide<IAnnouncementDirector>,
    IProvide<ClientRoster>,
    IProvide<ClientAdmin>,
    IProvide<ClientChat>,
    IProvide<INetwork>,
    IProvide<ISessionExit>,
    IProvide<ISfx>
{
    private const long KILLER = 1;
    private const long VICTIM = 2;

    private readonly FakeAnnouncementDirector _director = new();
    private readonly FakeNetwork _network = new();
    private readonly FakeSessionExit _sessionExit = new();

    [Export] private ClientRoster _roster = null!;
    [Export] private ClientAdmin _admin = null!;
    [Export] private ClientChat _chat = null!;
    [Export] private Sfx _sfx = null!;

    IAnnouncementDirector IProvide<IAnnouncementDirector>.Value() => _director;
    ClientRoster IProvide<ClientRoster>.Value() => _roster;
    ClientAdmin IProvide<ClientAdmin>.Value() => _admin;
    ClientChat IProvide<ClientChat>.Value() => _chat;
    INetwork IProvide<INetwork>.Value() => _network;
    ISessionExit IProvide<ISessionExit>.Value() => _sessionExit;
    ISfx IProvide<ISfx>.Value() => _sfx;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        // No server here, so typed chat goes nowhere instead of throwing.
        NetTransport.Send = static (_, _, _, _) => { };
        this.Provide();
    }

    private void OnFirstBlood() => Fire(Event(GameEventKind.FIRST_BLOOD));

    private void OnHumiliation() => Fire(Event(GameEventKind.HUMILIATION));

    private void OnShutdown() => Fire(Event(GameEventKind.SHUTDOWN, 5));

    private void OnRevenge() => Fire(Event(GameEventKind.REVENGE));

    private void OnTeamWipe() => Fire(Event(GameEventKind.TEAM_WIPE));

    private void OnSuicideBlast() => Fire(Suicide(1, SuicideCause.BLAST));

    private void OnSuicideFall() => Fire(Suicide(1, SuicideCause.FALL));

    private void OnSuicideMocked() => Fire(Suicide(3, SuicideCause.BLAST));

    private void OnDoubleKill() => Fire(Event(GameEventKind.MULTI_KILL, 2));

    private void OnTripleKill() => Fire(Event(GameEventKind.MULTI_KILL, 3));

    private void OnOverkill() => Fire(Event(GameEventKind.MULTI_KILL, 4));

    private void OnUltraKill() => Fire(Event(GameEventKind.MULTI_KILL, 5));

    private void OnMassacre() => Fire(Event(GameEventKind.MULTI_KILL, 6));

    private void OnCarnage() => Fire(Event(GameEventKind.MULTI_KILL, 7));

    private void OnBloodlust() => Fire(Event(GameEventKind.KILL_STREAK, 5));

    private void OnPunishment() => Fire(Event(GameEventKind.KILL_STREAK, 7));

    private void OnDominating() => Fire(Event(GameEventKind.KILL_STREAK, 9));

    private void OnMachineGod() => Fire(Event(GameEventKind.KILL_STREAK, 11));

    private void OnPsycho() => Fire(Event(GameEventKind.KILL_STREAK, 13));

    private void OnHolyShitCombo() => Fire(
        Event(GameEventKind.HOLY_SHIT, 3),
        Event(GameEventKind.MULTI_KILL, 3),
        Event(GameEventKind.KILL_STREAK, 9));

    private void OnLineOverflow()
    {
        for (int i = 0; i < 6; i++)
        {
            Fire(Event(GameEventKind.FIRST_BLOOD));
        }
    }

    private void OnMatchPoint3() => MatchPoint(3);

    private void OnMatchPoint1() => MatchPoint(1);

    private void OnMatchPointOff() => _director.SetMatchPoint(
        new MatchPointMsg(false, WinCondition.PLAYER_KILLS, 0));

    private void Fire(params GameEventMsg[] events) => _director.Fire(events);

    private void MatchPoint(byte remaining) => _director.SetMatchPoint(
        new MatchPointMsg(true, WinCondition.PLAYER_KILLS, remaining, KILLER));

    private static GameEventMsg Event(GameEventKind kind, byte magnitude = 0) =>
        new(kind, KILLER, VICTIM, magnitude);

    private static GameEventMsg Suicide(byte count, SuicideCause cause) =>
        new(GameEventKind.SUICIDE, KILLER, KILLER, count, (byte)cause);
}
