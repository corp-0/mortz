using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Servers;

namespace Mortz.Client.Menus;

[Meta(typeof(IAutoNode))]
public partial class ServerBrowser : Control
{
    private static readonly Color _noteColor = new(0.72f, 0.72f, 0.75f);
    private static readonly Color _warningColor = new(1f, 0.55f, 0.45f);

    [Signal] public delegate void BackRequestedEventHandler();

    [Export] private PackedScene _rowScene = null!;
    [Export] private Container _rows = null!;
    [Export] private Label _status = null!;
    [Export] private Button _joinButton = null!;
    [Export] private Button _favoriteButton = null!;
    [Export] private DirectConnectPanel _directPanel = null!;

    [Dependency] private ServerBrowserController Browser => this.DependOn<ServerBrowserController>();
    private readonly Dictionary<ServerEntry, ServerRow> _rowsByEntry = [];
    private bool _subscribed;

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Browser.Changed += Render;
        _directPanel.FindRequested += OnDirectFindRequested;
        _directPanel.JoinRequested += OnDirectJoinRequested;
        _directPanel.CancelRequested += OnDirectCancelRequested;
        VisibilityChanged += OnVisibilityChanged;
        _subscribed = true;
        Render();
    }

    public override void _ExitTree()
    {
        if (!_subscribed) { return; }
        Browser.Close();
        Browser.Changed -= Render;
        _directPanel.FindRequested -= OnDirectFindRequested;
        _directPanel.JoinRequested -= OnDirectJoinRequested;
        _directPanel.CancelRequested -= OnDirectCancelRequested;
        VisibilityChanged -= OnVisibilityChanged;
        _subscribed = false;
    }

    public void Open() => Browser.Open();
    public void OnRefreshPressed() => Browser.Refresh();
    public void OnJoinPressed() => Browser.JoinSelected();
    public void OnDirectConnectPressed()
    {
        Browser.OpenDirect();
        _directPanel.FocusAddress();
    }

    public void OnFavoritePressed()
    {
        if (Browser.Selected is ServerEntry entry)
        {
            Browser.ToggleFavorite(entry.Endpoint);
        }
    }

    public void OnBackPressed()
    {
        if (_directPanel.Visible)
        {
            Browser.CancelDirect();
            return;
        }
        Browser.Close();
        EmitSignal(SignalName.BackRequested);
    }

    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree())
        {
            Browser.Close();
        }
    }

    private void OnDirectFindRequested(string address, string port, string queryPort) =>
        Browser.FindDirect(address, port, queryPort);
    private void OnDirectJoinRequested(string address, string port) => Browser.JoinDirect(address, port);
    private void OnDirectCancelRequested() => Browser.CancelDirect();

    private void Render()
    {
        IReadOnlyList<ServerEntry> entries = Browser.Entries;
        HashSet<ServerEntry> current = new(entries);
        foreach ((ServerEntry entry, ServerRow row) in _rowsByEntry.ToArray())
        {
            if (!current.Contains(entry))
            {
                _rows.RemoveChild(row);
                row.QueueFree();
                _rowsByEntry.Remove(entry);
            }
        }
        for (int i = 0; i < entries.Count; i++)
        {
            ServerRow row = RowFor(entries[i]);
            _rows.MoveChild(row, i);
            row.Refresh();
            row.SetSelected(ReferenceEquals(entries[i], Browser.Selected));
        }
        RenderState();
    }

    private ServerRow RowFor(ServerEntry entry)
    {
        if (_rowsByEntry.TryGetValue(entry, out ServerRow? row))
        {
            return row;
        }
        row = _rowScene.Instantiate<ServerRow>();
        row.Bind(entry);
        row.Selected += () => Browser.Select(entry.Endpoint);
        row.FavoriteToggled += () => Browser.ToggleFavorite(entry.Endpoint);
        _rows.AddChild(row);
        _rowsByEntry.Add(entry, row);
        return row;
    }

    private void RenderState()
    {
        _joinButton.Disabled = Browser.Selected == null;
        _favoriteButton.Disabled = Browser.Selected is null or { CanToggleFavorite: false };
        _favoriteButton.Text = Browser.Selected is { IsFavorite: true } ? "Unfavorite" : "Favorite";
        string notice = Browser.Notice switch
        {
            BrowserNotice.FOUND => $"Found {Browser.NoticeEndpoint}. Star it to keep it in your list.",
            BrowserNotice.INCOMPATIBLE => "That server runs a different version of the game.",
            BrowserNotice.NO_RESPONSE => "That server did not answer. Refresh, or check the address.",
            BrowserNotice.PINNED => "The playtest server stays in your list.",
            _ => "",
        };
        _status.Text = Browser.DiscoveryStatus + (notice.Length == 0 ? "" : "\n" + notice);
        _status.AddThemeColorOverride("font_color", Browser.Notice is
            BrowserNotice.INCOMPATIBLE or BrowserNotice.NO_RESPONSE ? _warningColor : _noteColor);
        _directPanel.Render(Browser.DirectState, Browser.AddressError, Browser.DirectTarget);
    }
}
