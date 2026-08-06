namespace Mortz.Server.Phases;

/// <summary>Transition requests raised outside the phase's own Advance
/// (ready-up handlers, admin END_MATCH). Read and cleared once per Advance.</summary>
public sealed class PhaseControl
{
    private PhaseRequest _pending;

    public void Request(PhaseRequest request) => _pending = request;

    public PhaseRequest Take()
    {
        PhaseRequest taken = _pending;
        _pending = PhaseRequest.NONE;
        return taken;
    }
}
