using Mortz.Client.Audio;
using Mortz.Core.Net.Messages;
using Xunit;

namespace Mortz.Tests.Client;

public class KillAnnouncerTests
{
    [Fact]
    public void FirstBloodIsGlobal()
    {
        EliminationMsg msg = Message(killer: 7, EliminationFlags.FIRST_BLOOD);

        Assert.Equal(KillAnnouncer.Cue.FIRST_BLOOD,
            KillAnnouncer.SelectCue(msg, localId: 99));
    }

    [Fact]
    public void FirstBloodTakesPriorityOverOwned()
    {
        EliminationMsg msg = Message(killer: 7,
            EliminationFlags.FIRST_BLOOD | EliminationFlags.OWNED);

        Assert.Equal(KillAnnouncer.Cue.FIRST_BLOOD,
            KillAnnouncer.SelectCue(msg, localId: 7));
    }

    [Fact]
    public void OwnedRemainsPersonalToTheKiller()
    {
        EliminationMsg msg = Message(killer: 7, EliminationFlags.OWNED);

        Assert.Equal(KillAnnouncer.Cue.OWNED,
            KillAnnouncer.SelectCue(msg, localId: 7));
        Assert.Null(KillAnnouncer.SelectCue(msg, localId: 99));
    }

    [Fact]
    public void OrdinaryKillHasNoAnnouncement()
    {
        Assert.Null(KillAnnouncer.SelectCue(
            Message(killer: 7, EliminationFlags.NONE), localId: 7));
    }

    private static EliminationMsg Message(long killer, EliminationFlags flags) =>
        new(killer, VictimId: 8, flags, KillerKills: 1, VictimDeaths: 1,
            Team1Kills: 0, Team2Kills: 0);
}
