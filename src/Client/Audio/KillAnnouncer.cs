using Godot;
using Mortz.Core.Net.Messages;

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
        switch (SelectCue(msg, Multiplayer.GetUniqueId()))
        {
            case Cue.FIRST_BLOOD:
                Sfx.Play(Sfx.Sounds.FirstBlood);
                break;
            case Cue.OWNED:
                Sfx.Play(Sfx.Sounds.Owned);
                break;
        }
    }

    internal static Cue? SelectCue(EliminationMsg msg, long localId) =>
        (msg.Flags & EliminationFlags.FIRST_BLOOD) != 0
            ? Cue.FIRST_BLOOD
            : msg.KillerId == localId && (msg.Flags & EliminationFlags.OWNED) != 0
                ? Cue.OWNED
                : null;
}
