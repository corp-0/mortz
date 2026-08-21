using System.Collections.Immutable;
using Godot;
using Mortz.Content;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorTopBar : PanelContainer
{
    [Export] private Button _back = null!;
    [Export] private Label _status = null!;
    [Export] private Button _reload = null!;
    [Export] private Button _save = null!;
    [Export] private Button _zoomOut = null!;
    [Export] private Button _zoom = null!;
    [Export] private Button _zoomIn = null!;
    [Export] private Button _frame = null!;

    public event Action? BackRequested;
    public event Action? ReloadRequested;
    public event Action? SaveRequested;
    public event Action? ZoomOutRequested;
    public event Action? ZoomResetRequested;
    public event Action? ZoomInRequested;
    public event Action? FrameMapRequested;

    public override void _Ready()
    {
        _back.Pressed += RequestBack;
        _reload.Pressed += RequestReload;
        _save.Pressed += RequestSave;
        _zoomOut.Pressed += RequestZoomOut;
        _zoom.Pressed += RequestZoomReset;
        _zoomIn.Pressed += RequestZoomIn;
        _frame.Pressed += RequestFrameMap;
    }

    public override void _ExitTree()
    {
        _back.Pressed -= RequestBack;
        _reload.Pressed -= RequestReload;
        _save.Pressed -= RequestSave;
        _zoomOut.Pressed -= RequestZoomOut;
        _zoom.Pressed -= RequestZoomReset;
        _zoomIn.Pressed -= RequestZoomIn;
        _frame.Pressed -= RequestFrameMap;
    }

    public void Apply(MapEditorSnapshot snapshot)
    {
        _save.Disabled = !snapshot.CanSave;
        int blocking = snapshot.Diagnostics.Count(diagnostic =>
            diagnostic.Severity == ContentDiagnosticSeverity.ERROR);
        _save.TooltipText = blocking > 0
            ? $"Fix {blocking} problem{(blocking == 1 ? "" : "s")} before saving."
            : snapshot.Dirty
                ? "Save map"
                : "No unsaved changes";
        ApplyDiagnostics(snapshot.Diagnostics, snapshot.Dirty);
    }

    public void ApplyZoom(float zoom) => _zoom.Text = $"{zoom * 100:0}%";

    public void SetCompact(bool compact)
    {
        _zoomOut.Visible = !compact;
        _zoom.Visible = !compact;
        _zoomIn.Visible = !compact;
        _frame.Visible = !compact;
    }

    public void ShowStatus(MapEditorStatus status)
    {
        _status.Text = status.Message;
        _status.Modulate = status.IsError ? new Color(1f, 0.45f, 0.4f) : Colors.White;
    }

    private void ApplyDiagnostics(ImmutableArray<ContentDiagnostic> diagnostics, bool dirty)
    {
        if (diagnostics.IsEmpty)
        {
            _status.Text = dirty ? "Unsaved changes" : string.Empty;
            _status.TooltipText = string.Empty;
            _status.Modulate = Colors.White;
            return;
        }

        int errors = diagnostics.Count(diagnostic =>
            diagnostic.Severity == ContentDiagnosticSeverity.ERROR);
        int warnings = diagnostics.Length - errors;
        _status.Text = $"{Count(errors, "error")}, {Count(warnings, "warning")}";
        _status.TooltipText = string.Join("\n", diagnostics.Select(diagnostic =>
            $"{diagnostic.Severity}: {diagnostic.Message}"));
        _status.Modulate = errors > 0
            ? new Color(1f, 0.45f, 0.4f)
            : new Color(1f, 0.8f, 0.35f);
    }

    private void RequestBack() => BackRequested?.Invoke();
    private void RequestReload() => ReloadRequested?.Invoke();
    private void RequestSave() => SaveRequested?.Invoke();
    private void RequestZoomOut() => ZoomOutRequested?.Invoke();
    private void RequestZoomReset() => ZoomResetRequested?.Invoke();
    private void RequestZoomIn() => ZoomInRequested?.Invoke();
    private void RequestFrameMap() => FrameMapRequested?.Invoke();

    private static string Count(int count, string word) =>
        $"{count} {word}{(count == 1 ? "" : "s")}";
}
