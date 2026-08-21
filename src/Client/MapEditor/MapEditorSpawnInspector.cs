using Godot;
using Mortz.Content;
using Mortz.Core.Match.Teams;

namespace Mortz.Client.MapEditor;

public sealed record MapEditorSpawnInspectorValue(
    MapEditorSpawnId Id,
    MapSpawnPoint Spawn,
    int Number);

public partial class MapEditorSpawnInspector : ScrollContainer
{
    [Export] private Label _title = null!;
    [Export] private MapEditorInspectorField _x = null!;
    [Export] private MapEditorInspectorField _y = null!;
    [Export] private OptionButton _team = null!;
    [Export] private Button _delete = null!;
    private MapEditorInspectorField[] _fields = null!;
    private MapEditorSpawnInspectorValue? _value;
    private bool _applying;

    public override void _Ready()
    {
        _x.Configure("X", "X", "Spawn X position");
        _y.Configure("Y", "Y", "Spawn Y position");
        _fields = [_x, _y];
        foreach (MapEditorInspectorField field in _fields)
        {
            field.PreviewRequested += Preview;
            field.CommitRequested += Commit;
            field.CancelRequested += CancelFromField;
        }

        _team.ItemSelected += CommitTeam;
        _delete.Pressed += Remove;
    }

    public MapEditorSpawnId? SelectedId => _value?.Id;

    public event Action<MapEditorSpawnId, MapSpawnPoint>? PreviewRequested;
    public event Action<MapEditorSpawnId, MapSpawnPoint>? CommitRequested;
    public event Action<MapEditorSpawnId>? CancelRequested;
    public event Action<MapEditorSpawnId>? RemoveRequested;

    public void Apply(MapEditorSpawnInspectorValue value)
    {
        _value = value;
        _applying = true;
        _title.Text = $"Spawn {value.Number}";
        _x.Apply(value.Spawn.X.ToString());
        _y.Apply(value.Spawn.Y.ToString());
        _team.Select(value.Spawn.Team switch
        {
            Team.BLUE => 1,
            Team.RED => 2,
            _ => 0,
        });
        _applying = false;
    }

    public void CancelDraft(bool suppressFocusCommit = true)
    {
        bool dirty = _fields.Any(field => field.Dirty);
        foreach (MapEditorInspectorField field in _fields)
        {
            field.Cancel(suppressFocusCommit);
        }

        if (dirty && _value != null)
            CancelRequested?.Invoke(_value.Id);
    }

    private void Remove()
    {
        if (_value == null)
            return;
        CancelDraft();
        RemoveRequested?.Invoke(_value.Id);
    }

    private void Preview()
    {
        if (TryRead(out MapSpawnPoint spawn) && _value != null)
            PreviewRequested?.Invoke(_value.Id, spawn);
    }

    private void Commit()
    {
        if (!TryRead(out MapSpawnPoint spawn) || _value == null)
            return;
        foreach (MapEditorInspectorField field in _fields)
        {
            field.MarkCommitted();
        }

        CommitRequested?.Invoke(_value.Id, spawn);
    }

    private void CommitTeam(long _)
    {
        if (!_applying && _value != null && TryRead(out MapSpawnPoint spawn))
            CommitRequested?.Invoke(_value.Id, spawn);
    }

    private void CancelFromField()
    {
        foreach (MapEditorInspectorField field in _fields)
        {
            field.Cancel(true);
        }

        if (_value != null)
            CancelRequested?.Invoke(_value.Id);
    }

    private bool TryRead(out MapSpawnPoint spawn)
    {
        spawn = default;
        _x.SetError(null);
        _y.SetError(null);
        if (!int.TryParse(_x.Editor.Text, out int x))
        {
            _x.SetError("Enter a whole number.");
            return false;
        }

        if (!int.TryParse(_y.Editor.Text, out int y))
        {
            _y.SetError("Enter a whole number.");
            return false;
        }

        Team? team = _team.Selected switch
        {
            1 => Team.BLUE,
            2 => Team.RED,
            _ => null,
        };
        spawn = new MapSpawnPoint(x, y, team);
        return true;
    }
}
