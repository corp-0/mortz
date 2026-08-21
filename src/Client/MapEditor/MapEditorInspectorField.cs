using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorInspectorField : VBoxContainer
{
    [Export] private Label _label = null!;
    [Export] private LineEdit _editor = null!;
    [Export] private Label _error = null!;

    private string _committed = string.Empty;
    private bool _applying;
    private bool _dirty;
    private bool _suppressFocusCommit;

    public override void _Ready()
    {
        _editor.TextChanged += OnTextChanged;
        _editor.TextSubmitted += _ => RequestCommit();
        _editor.FocusExited += OnFocusExited;
        _editor.GuiInput += OnGuiInput;
    }

    public LineEdit Editor => _editor;
    public bool Dirty => _dirty;

    public event Action? PreviewRequested;
    public event Action? CommitRequested;
    public event Action? CancelRequested;

    public void Configure(string name, string label, string accessibilityName)
    {
        Name = name;
        _label.Text = label;
        _editor.Name = "Value";
        _editor.AccessibilityName = accessibilityName;
        _error.Name = "Error";
    }

    public void Apply(string value, bool force = false)
    {
        _committed = value;
        if (!force && _dirty && _editor.HasFocus())
            return;

        int caret = _editor.CaretColumn;
        _applying = true;
        _editor.Text = value;
        _applying = false;
        _dirty = false;
        SetError(null);
        if (_editor.HasFocus())
        {
            _editor.CaretColumn = Math.Min(caret, value.Length);
        }
    }

    public void MarkCommitted()
    {
        _dirty = false;
        _suppressFocusCommit = true;
    }

    public void Cancel(bool suppressFocusCommit)
    {
        _suppressFocusCommit |= suppressFocusCommit;
        Apply(_committed, true);
    }

    public void SetError(string? error)
    {
        _error.Text = error ?? string.Empty;
        _error.Visible = error != null;
    }

    private void OnTextChanged(string _)
    {
        if (_applying)
            return;
        _dirty = true;
        _suppressFocusCommit = false;
        PreviewRequested?.Invoke();
    }

    private void OnFocusExited()
    {
        if (_suppressFocusCommit)
        {
            _suppressFocusCommit = false;
            return;
        }

        if (_dirty)
            Callable.From(RequestCommit).CallDeferred();
    }

    private void OnGuiInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            return;
        Cancel(true);
        CancelRequested?.Invoke();
        _editor.AcceptEvent();
    }

    private void RequestCommit()
    {
        if (_dirty)
            CommitRequested?.Invoke();
    }
}
