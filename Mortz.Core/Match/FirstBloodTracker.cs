namespace Mortz.Core.Match;

/// <summary>First blood, once per match. Reset by the server on match start.</summary>
public sealed class FirstBloodTracker
{
    private bool _claimed;

    public bool TryClaim(bool creditedKill)
    {
        if (_claimed || !creditedKill)
            return false;
        _claimed = true;
        return true;
    }

    public void Reset() => _claimed = false;
}
