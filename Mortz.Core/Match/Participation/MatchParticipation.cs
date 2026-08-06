namespace Mortz.Core.Match.Participation;

public readonly record struct MatchParticipation(
    MatchSeat Seat,
    MatchActivity Activity,
    SpectateReason Reason,
    int ReturnTick)
{
    public static MatchParticipation Active =>
        new(MatchSeat.PLAYER, MatchActivity.ACTIVE, SpectateReason.NONE, -1);

    public static MatchParticipation JipSpectator =>
        new(MatchSeat.SPECTATOR, MatchActivity.SPECTATING, SpectateReason.JIP, -1);

    public bool IsValid =>
        Enum.IsDefined(Seat) && Enum.IsDefined(Activity) && Enum.IsDefined(Reason) &&
        (Activity == MatchActivity.ACTIVE
            ? Seat == MatchSeat.PLAYER && Reason == SpectateReason.NONE && ReturnTick == -1
            : Reason != SpectateReason.NONE && ReturnTick >= -1);
}
