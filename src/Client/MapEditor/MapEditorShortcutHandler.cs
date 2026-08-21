using Godot;

namespace Mortz.Client.MapEditor;

public interface IMapEditorShortcutTarget
{
    bool IsTextEditing { get; }
    bool IsCreatingPolygon { get; }
    void Save();
    bool Cancel();
    void Undo();
    void Redo();
    void CycleSnap(bool gridOnly);
    void CompletePolygon();
    void RemovePolygonVertex();
    bool DeleteSelection();
    bool DuplicateSelection();
    bool SelectDomain(MapEditorEditDomain domain);
    bool SelectTool(MapEditorTool tool);
    bool SelectShape(bool rectangle);
    bool SelectGeometryTool(MapEditorTool tool);
    bool SelectSpawnTool();
    bool FrameAll(bool selectionOnly);
}

public sealed class MapEditorShortcutHandler(IMapEditorShortcutTarget target)
{
    public bool Handle(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } saveKey &&
            (saveKey.CtrlPressed || saveKey.MetaPressed) && saveKey.Keycode == Key.S)
        {
            target.Save();
            return true;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            return target.Cancel();
        }

        if (target.IsTextEditing)
        {
            return false;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } historyKey &&
            (historyKey.CtrlPressed || historyKey.MetaPressed) && historyKey.Keycode == Key.Z)
        {
            if (historyKey.ShiftPressed)
            {
                target.Redo();
            }
            else
            {
                target.Undo();
            }

            return true;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.G } gridKey &&
            !gridKey.CtrlPressed && !gridKey.MetaPressed && !gridKey.AltPressed)
        {
            target.CycleSnap(gridKey.ShiftPressed);
            return true;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter } &&
            target.IsCreatingPolygon)
        {
            target.CompletePolygon();
            return true;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Backspace } &&
            target.IsCreatingPolygon)
        {
            target.RemovePolygonVertex();
            return true;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } redoKey &&
            (redoKey.CtrlPressed || redoKey.MetaPressed) && redoKey.Keycode == Key.Y)
        {
            target.Redo();
            return true;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete })
        {
            return target.DeleteSelection();
        }

        if (@event is InputEventKey { Pressed: true, Echo: false } duplicateKey &&
            (duplicateKey.CtrlPressed || duplicateKey.MetaPressed) && duplicateKey.Keycode == Key.D)
        {
            return target.DuplicateSelection();
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } shortcut ||
            shortcut.CtrlPressed || shortcut.MetaPressed || shortcut.AltPressed)
        {
            return false;
        }

        return shortcut.Keycode switch
        {
            Key.Key1 => target.SelectDomain(MapEditorEditDomain.GEOMETRY),
            Key.Key2 => target.SelectDomain(MapEditorEditDomain.ZONES),
            Key.Key3 => target.SelectDomain(MapEditorEditDomain.SPAWNS),
            Key.V => target.SelectTool(MapEditorTool.SELECT),
            Key.R => target.SelectShape(rectangle: true),
            Key.E => target.SelectShape(rectangle: false),
            Key.P => target.SelectGeometryTool(MapEditorTool.BRUSH_POLYGON),
            Key.S => target.SelectSpawnTool(),
            Key.F => target.FrameAll(shortcut.ShiftPressed),
            _ => false,
        };
    }
}
