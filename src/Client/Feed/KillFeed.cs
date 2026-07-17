using Godot;
using Mortz.Client.Roster;

namespace Mortz.Client.Feed;

/// <summary>Persistent owner of the kill feed stream. The composition root
/// decides which displays consume the lines.</summary>
public partial class KillFeed : Node
{
    [Export] private ClientRoster _roster = null!;
    private KillFeedSession? _session;

    public event Action<string>? LineAdded;

    public override void _Ready()
    {
        _session = new KillFeedSession(_roster.NameOf);
        _session.LineAdded += OnLine;
        _session.Subscribe();
    }

    public override void _ExitTree()
    {
        if (_session == null)
            return;
        _session.LineAdded -= OnLine;
        _session.Dispose();
        _session = null;
    }

    private void OnLine(string line) => LineAdded?.Invoke(line);
}
