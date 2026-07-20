using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Audio;

[Meta(typeof(IAutoNode))]
public partial class KillAnnouncer : Node
{
    internal enum Cue
    {
        FIRST_BLOOD,
        OWNED,
    }

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady() => EliminationMsg.Received += OnElimination;

    public void OnExitTree() => EliminationMsg.Received -= OnElimination;

    private void OnElimination(EliminationMsg msg)
    {
        switch (SelectCue(msg, Network.LocalPeerId))
        {
            case Cue.FIRST_BLOOD:
                Sfx.Play(Sfx.Sounds.FirstBlood);
                break;
            case Cue.OWNED:
                Sfx.Play(Sfx.Sounds.Owned);
                break;
        }
    }

    internal static Cue? SelectCue(EliminationMsg msg, long localId)
    {
        if (msg.Flags.HasFlag(EliminationFlags.FIRST_BLOOD))
            return Cue.FIRST_BLOOD;
        if ((msg.KillerId == localId || msg.VictimId == localId) &&
            msg.Flags.HasFlag(EliminationFlags.OWNED))
            return Cue.OWNED;
        return null;
    }
}
