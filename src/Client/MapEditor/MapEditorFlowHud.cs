using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;

namespace Mortz.Client.MapEditor;

[Meta(typeof(IAutoNode))]
public partial class MapEditorFlowHud : Control
{
    private static readonly Color _errorColor = new(1f, 0.55f, 0.45f);

    [Export] private Control _packScreen = null!;
    [Export] private Control _mapScreen = null!;
    [Export] private VBoxContainer _packRows = null!;
    [Export] private VBoxContainer _mapRows = null!;
    [Export] private Label _packEmpty = null!;
    [Export] private Label _mapEmpty = null!;
    [Export] private Label _mapTitle = null!;
    [Export] private Label _mapHint = null!;
    [Export] private Label _status = null!;
    [Export] private ConfirmationDialog _createPackDialog = null!;
    [Export] private LineEdit _packName = null!;
    [Export] private LineEdit _packId = null!;
    [Export] private ConfirmationDialog _createMapDialog = null!;
    [Export] private LineEdit _mapName = null!;
    [Export] private LineEdit _mapId = null!;
    [Export] private SpinBox _mapWidth = null!;
    [Export] private SpinBox _mapHeight = null!;
    [Export] private SpinBox _suggestedPlayers = null!;

    [Dependency]
    private MapEditorFlow Flow => this.DependOn<MapEditorFlow>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Flow.PacksShown += ShowPacks;
        Flow.MapsShown += ShowMaps;
        Flow.StatusChanged += ShowStatus;
        Flow.Start();
    }

    public override void _ExitTree()
    {
        Flow.PacksShown -= ShowPacks;
        Flow.MapsShown -= ShowMaps;
        Flow.StatusChanged -= ShowStatus;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
            return;
        if (_mapScreen.Visible)
            Flow.BackToPacks();
        else
            Flow.Exit();
        GetViewport().SetInputAsHandled();
    }

    public void HideForEditor()
    {
        Hide();
        SetProcessUnhandledInput(false);
    }

    public void OnCreatePackPressed()
    {
        _packName.Clear();
        _packId.Clear();
        _createPackDialog.PopupCentered();
        _packName.GrabFocus();
    }

    public void OnCreatePackConfirmed() =>
        Flow.CreatePack(_packName.Text, _packId.Text);

    public void OnCreateMapPressed()
    {
        _mapName.Clear();
        _mapId.Clear();
        _createMapDialog.PopupCentered();
        _mapName.GrabFocus();
    }

    public void OnCreateMapConfirmed() => Flow.CreateMap(
        _mapId.Text,
        _mapName.Text,
        (int)_mapWidth.Value,
        (int)_mapHeight.Value,
        (int)_suggestedPlayers.Value);

    public void OnPackBackPressed() => Flow.Exit();
    public void OnMapBackPressed() => Flow.BackToPacks();

    private void ShowPacks(IReadOnlyList<ContentPackChoice> packs)
    {
        Show();
        SetProcessUnhandledInput(true);
        _packScreen.Show();
        _mapScreen.Hide();
        Rebuild(_packRows, packs.Count, index => Flow.SelectPack(index), index =>
        {
            ContentPackChoice pack = packs[index];
            string maps = pack.MapCount == 1 ? "1 map" : $"{pack.MapCount} maps";
            return $"{pack.Definition.Manifest.Name}\n{pack.Definition.Manifest.Id} - {maps}";
        });
        _packEmpty.Visible = packs.Count == 0;
    }

    private void ShowMaps(ContentPackChoice pack, IReadOnlyList<MapChoice> maps)
    {
        Show();
        SetProcessUnhandledInput(true);
        _packScreen.Hide();
        _mapScreen.Show();
        _mapTitle.Text = pack.Definition.Manifest.Name;
        _mapHint.Text = $"Choose a map from {pack.Definition.Manifest.Id}, or create a blank one.";
        Rebuild(_mapRows, maps.Count, index => Flow.SelectMap(index), index =>
        {
            MapChoice map = maps[index];
            int players = map.Definition.Manifest.SuggestedPlayers;
            return $"{map.Definition.Manifest.Name}\n{map.Definition.Id} - {players} suggested players";
        });
        _mapEmpty.Visible = maps.Count == 0;
    }

    private static void Rebuild(Container rows, int count, Action<int> selected,
        Func<int, string> text)
    {
        foreach (Node child in rows.GetChildren())
        {
            if (child.Name == "Empty")
                continue;
            rows.RemoveChild(child);
            child.QueueFree();
        }
        for (int i = 0; i < count; i++)
        {
            int index = i;
            Button button = new()
            {
                Text = text(index),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 68),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            button.Pressed += () => selected(index);
            rows.AddChild(button);
        }
    }

    private void ShowStatus(string message, bool error)
    {
        _status.Text = message;
        _status.Modulate = error ? _errorColor : Colors.White;
    }
}
