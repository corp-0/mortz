using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Servers;
using Mortz.Client.Settings;
using Mortz.Core.Net.Query;

namespace Mortz.Client.Menus;

/// <summary>
/// The join screen. Favorites and the pinned playtest server are probed on
/// entry and on every refresh; direct connect and LAN discovery add rows to
/// the same list.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class ServerBrowser : Control
{
    private static readonly Color _noteColor = new(0.72f, 0.72f, 0.75f);
    private static readonly Color _warningColor = new(1f, 0.55f, 0.45f);

    [Signal] public delegate void JoinRequestedEventHandler(string address, int port);
    [Signal] public delegate void BackRequestedEventHandler();

    [Export] private PackedScene _rowScene = null!;
    [Export] private ServerProbe _probe = null!;
    [Export] private Container _rows = null!;
    [Export] private Label _status = null!;
    [Export] private Button _joinButton = null!;
    [Export] private Button _favoriteButton = null!;
    [Export] private DirectConnectPanel _directPanel = null!;

    private readonly Dictionary<ServerEndpoint, ServerRow> _rowsByEndpoint = [];
    private ServerList _list = new();
    private ServerEntry? _selected;

    /// <summary>What direct connect is waiting on. Lives here because the
    /// probe outcome lands here, not in the panel.</summary>
    private ServerEndpoint? _directTarget;

    [Dependency]
    private ClientSettings Settings => this.DependOn<ClientSettings>();

    public override void _Notification(int what) => this.Notify(what);

    public override void _Ready()
    {
        _probe.Replied += OnReplied;
        _probe.TimedOut += OnTimedOut;
        _probe.Discovered += OnDiscovered;
        _directPanel.FindRequested += OnDirectFindRequested;
        _directPanel.JoinRequested += OnDirectJoinRequested;
        UpdateButtons();
    }

    public override void _ExitTree()
    {
        _probe.Replied -= OnReplied;
        _probe.TimedOut -= OnTimedOut;
        _probe.Discovered -= OnDiscovered;
        _directPanel.FindRequested -= OnDirectFindRequested;
        _directPanel.JoinRequested -= OnDirectJoinRequested;
    }

    public void Open()
    {
        _list = new ServerList(Settings.Favorites);
        _selected = null;
        RebuildRows();
        Refresh();
    }

    // ---- button handlers (connected in ServerBrowser.tscn) ----

    public void OnRefreshPressed() => Refresh();

    public void OnJoinPressed()
    {
        if (_selected is not { } entry)
            return;
        switch (entry.Status)
        {
            case ServerStatus.ONLINE:
                EmitSignal(SignalName.JoinRequested, entry.Endpoint.Address, entry.Endpoint.Port);
                return;
            case ServerStatus.INCOMPATIBLE:
                Warn("That server runs a different version of the game.");
                return;
            default:
                Warn("That server did not answer. Refresh, or check the address.");
                return;
        }
    }

    public void OnFavoritePressed()
    {
        if (_selected is { } entry)
            ToggleFavorite(entry);
    }

    public void OnBackPressed()
    {
        if (_directPanel.Visible)
        {
            _directPanel.Hide();
            return;
        }
        EmitSignal(SignalName.BackRequested);
    }

    public void OnDirectConnectPressed() => _directPanel.Open();

    private void OnDirectFindRequested(string address, int port)
    {
        ServerEndpoint endpoint = new(address, port);
        _directTarget = endpoint;
        _probe.Probe(endpoint);
    }

    private void OnDirectJoinRequested(string address, int port) =>
        EmitSignal(SignalName.JoinRequested, address, port);

    private void FinishDirectConnect(ServerEndpoint endpoint)
    {
        _directTarget = null;
        _directPanel.Found();
        if (_list.Find(endpoint) is { } entry)
            Select(entry);
        Note($"Found {endpoint}. Star it to keep it in your list.");
    }

    private void Refresh()
    {
        Note("");
        _probe.Cancel();
        _directTarget = null;
        _list.MarkProbing();
        foreach (ServerRow row in _rowsByEndpoint.Values)
        {
            row.Refresh();
        }
        _probe.Probe(_list.Entries.Select(entry => entry.Endpoint));
        _probe.DiscoverLan();
        UpdateButtons();
    }

    private void OnReplied(ServerProbeReply reply)
    {
        ApplyReply(reply);
        if (reply.Endpoint == _directTarget)
            FinishDirectConnect(reply.Endpoint);
        UpdateButtons();
    }

    private void OnDiscovered(ServerProbeReply reply) => ApplyReply(reply);

    private void ApplyReply(ServerProbeReply reply)
    {
        bool wasListed = _list.Find(reply.Endpoint) != null;
        _list.ApplyReply(reply.Endpoint, reply.Info, reply.PingMs);
        if (wasListed)
            RowFor(reply.Endpoint)?.Refresh();
        else if (_list.Find(reply.Endpoint) is { } added)
            InsertRow(added);
    }

    private void OnTimedOut(ServerEndpoint endpoint)
    {
        _list.ApplyTimeout(endpoint);
        RowFor(endpoint)?.Refresh();
        if (endpoint == _directTarget)
        {
            _directTarget = null;
            _directPanel.NotFound();
        }
        UpdateButtons();
    }

    private void RebuildRows()
    {
        foreach (Node row in _rows.GetChildren())
        {
            row.QueueFree();
        }
        _rowsByEndpoint.Clear();
        foreach (ServerEntry entry in _list.Entries)
        {
            AddRow(entry);
        }
        UpdateButtons();
    }

    /// <summary>Slots one new entry into its group. Rebuilding every row per
    /// discovery packet would free rows mid signal dispatch.</summary>
    private void InsertRow(ServerEntry entry)
    {
        ServerRow row = AddRow(entry);
        IReadOnlyList<ServerEntry> ordered = _list.Entries;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ReferenceEquals(ordered[i], entry))
            {
                _rows.MoveChild(row, i);
                break;
            }
        }
    }

    private ServerRow AddRow(ServerEntry entry)
    {
        ServerRow row = _rowScene.Instantiate<ServerRow>();
        row.Bind(entry);
        row.Selected += () => Select(entry);
        row.FavoriteToggled += () => ToggleFavorite(entry);
        _rows.AddChild(row);
        _rowsByEndpoint[entry.Endpoint] = row;
        row.SetSelected(ReferenceEquals(entry, _selected));
        return row;
    }

    private ServerRow? RowFor(ServerEndpoint endpoint) =>
        _rowsByEndpoint.GetValueOrDefault(endpoint);

    private void Select(ServerEntry entry)
    {
        _selected = entry;
        foreach ((ServerEndpoint endpoint, ServerRow row) in _rowsByEndpoint)
        {
            row.SetSelected(endpoint == entry.Endpoint);
        }
        Note("");
        UpdateButtons();
    }

    private void ToggleFavorite(ServerEntry entry)
    {
        if (!_list.ToggleFavorite(entry))
        {
            Note("The playtest server stays in your list.");
            return;
        }
        Settings.SetFavorites(_list.Favorites);
        RowFor(entry.Endpoint)?.Refresh();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _joinButton.Disabled = _selected == null;
        _favoriteButton.Disabled = _selected is null or { CanToggleFavorite: false };
        _favoriteButton.Text = _selected is { IsFavorite: true } ? "Unfavorite" : "Favorite";
    }

    private void Note(string text) => SetStatus(text, _noteColor);

    private void Warn(string text) => SetStatus(text, _warningColor);

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }
}
