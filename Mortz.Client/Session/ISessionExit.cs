namespace Mortz.Client.Session;

/// <summary>Leaving the current server: tears the session down and returns to
/// the main menu.</summary>
public interface ISessionExit
{
    void LeaveSession(string reason);
}
