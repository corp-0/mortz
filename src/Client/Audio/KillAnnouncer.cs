using Godot;
using Mortz.Core.Net.Messages;
using Mortz.Net;

namespace Mortz.Client.Audio;

public partial class KillAnnouncer : Node
{
    internal enum Cue
    {
        FIRST_BLOOD,
        OWNED,
    }

    public override void _Ready() => EliminationMsg.Received += OnElimination;

    public override void _ExitTree() => EliminationMsg.Received -= OnElimination;

    private void OnElimination(EliminationMsg msg)
    {
        switch (SelectCue(msg, NetworkManager.Instance.LocalPeerId))
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
        if ((msg.Flags & EliminationFlags.FIRST_BLOOD) != 0)
            return Cue.FIRST_BLOOD;
        if (msg.KillerId == localId && (msg.Flags & EliminationFlags.OWNED) != 0)
            return Cue.OWNED;
        return null;
    }
}
