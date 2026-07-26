using Godot;
using Mortz.Client.Session;

namespace Mortz.Client.Debug;

public sealed class FakeSessionExit : ISessionExit
{
    public void LeaveSession(string reason) => GD.Print($"LeaveSession: {reason}");
}
