using System.Text;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Setup;
using Mortz.Client.Ui;
using Mortz.Core.Admin;
using Mortz.Core.Match;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Net;
using Mortz.Core.Net.Lobby;
using Mortz.Shared;

namespace Mortz.Client.Menus;

[Meta(typeof(IAutoNode))]
public partial class LobbySettingsPanel : PanelContainer
{
    [Export] private OptionButton _modePicker = null!;
    [Export] private OptionButton _mapPicker = null!;
    [Export] private TextureRect _mapPreview = null!;
    [Export] private Label _mapStatus = null!;
    [Export] private UiPropertySheet _rulesSheet = null!;
    [Export] private UiPropertySheet _physicsSheet = null!;
    [Export] private UiPropertySheet _combatSheet = null!;

    private readonly List<string> _mapIds = [];
    private readonly List<string> _modeIds = [];
    private MatchConfig _config = new();
    private bool _applyingState;
    // UpdateEditing also runs before Setup resolves.
    private LobbySelection? _selection;
    private bool _subscribed;
    private string _previewMapId = "";
    private string _previewMapHash = "";

    [Dependency] private ClientAdmin Admin => this.DependOn<ClientAdmin>();

    [Dependency] private MatchSetup Setup => this.DependOn<MatchSetup>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _mapPicker.ItemSelected += OnMapSelected;
        _modePicker.ItemSelected += OnModeSelected;
        _rulesSheet.Build(ModeRulesUiMetadata.Categories, _config.Rules, OnConfigEdited);
        _physicsSheet.Build(PhysicsUiMetadata.Categories, _config.Physics, OnConfigEdited);
        _combatSheet.Build(CombatUiMetadata.Categories, _config.Combat, OnConfigEdited);
        UpdateEditing(isAdmin: false);
    }

    public void OnResolved()
    {
        Setup.SettingsChanged += OnSetupChanged;
        Admin.AdminChanged += OnAdminChanged;
        _subscribed = true;
        OnSetupChanged();
    }

    public void OnExitTree()
    {
        _mapPicker.ItemSelected -= OnMapSelected;
        _modePicker.ItemSelected -= OnModeSelected;
        if (!_subscribed)
            return;
        Setup.SettingsChanged -= OnSetupChanged;
        Admin.AdminChanged -= OnAdminChanged;
        _subscribed = false;
    }

    private void OnSetupChanged()
    {
        _selection = Setup.Selection;
        if (Setup.SettingsError is { } error)
        {
            _mapStatus.Text = error;
            return;
        }
        if (_selection is not { } selection)
        {
            UpdateEditing(Admin.IsAdmin);
            return;
        }

        _config = Setup.CopyConfig();
        ApplyMapOptions(selection);
        ApplyModeOptions(selection);
        _rulesSheet.UpdateModel(_config.Rules);
        _physicsSheet.UpdateModel(_config.Physics);
        _combatSheet.UpdateModel(_config.Combat);
        UpdateEditing(Admin.IsAdmin);
        UpdatePreview(selection);
    }

    private void ApplyMapOptions(LobbySelection selection)
    {
        _applyingState = true;
        _mapPicker.Clear();
        _mapIds.Clear();
        int selected = -1;
        foreach (ContentOption option in selection.Maps.Options)
        {
            if (option.Id == selection.MapId)
                selected = _mapIds.Count;
            _mapIds.Add(option.Id);
            _mapPicker.AddItem(option.Name);
        }
        if (selected >= 0)
            _mapPicker.Select(selected);
        _applyingState = false;
    }

    /// <summary>A null mode means no official mode matches the config, which is
    /// what the disabled Custom item is for.</summary>
    private void ApplyModeOptions(LobbySelection selection)
    {
        _applyingState = true;
        _modePicker.Clear();
        _modeIds.Clear();
        int selected = -1;
        foreach (ContentOption option in selection.Modes.Options)
        {
            if (option.Id == selection.ModeId)
                selected = _modeIds.Count;
            _modeIds.Add(option.Id);
            _modePicker.AddItem(option.Name);
        }
        if (selection.ModeId == null)
        {
            selected = _modePicker.ItemCount;
            _modePicker.AddItem("Custom");
            _modePicker.SetItemDisabled(selected, true);
        }
        _modePicker.Select(selected);
        _applyingState = false;
    }

    private void OnModeSelected(long index)
    {
        if (_applyingState || !Admin.IsAdmin || index < 0)
            return;
        if (index >= _modeIds.Count)
            return;
        string modeId = _modeIds[(int)index];
        byte[] payload = Encoding.UTF8.GetBytes(modeId);
        if (Admin.TrySignAdminAction(AdminAction.SET_LOBBY_MODE, payload,
                out ulong sequence, out byte[] tag))
        {
            new LobbyModeUpdateMsg(modeId, sequence, tag).SendToServer();
        }
    }

    private void OnConfigEdited()
    {
        if (!Admin.IsAdmin)
            return;
        byte[] payload = _config.ToBytes();
        if (Admin.TrySignAdminAction(AdminAction.SET_LOBBY_RULES, payload,
                out ulong sequence, out byte[] tag))
        {
            new LobbyRulesUpdateMsg(payload, sequence, tag).SendToServer();
        }
    }

    private void OnMapSelected(long index)
    {
        if (_applyingState || !Admin.IsAdmin || index < 0 || index >= _mapIds.Count)
            return;
        string mapId = _mapIds[(int)index];
        byte[] payload = Encoding.UTF8.GetBytes(mapId);
        if (Admin.TrySignAdminAction(AdminAction.SET_LOBBY_MAP, payload,
                out ulong sequence, out byte[] tag))
        {
            new LobbyMapUpdateMsg(mapId, sequence, tag).SendToServer();
        }
    }

    private void UpdatePreview(LobbySelection selection)
    {
        if (_previewMapId == selection.MapId && _previewMapHash == selection.MapHash)
            return;
        _previewMapId = selection.MapId;
        _previewMapHash = selection.MapHash;
        MapPackage? map = GameContent.Load()?.LoadMap(selection.MapId);
        if (map == null)
        {
            _mapPreview.Texture = null;
            _mapStatus.Text =
                $"Map '{selection.MapId}' is not installed locally; preview unavailable.";
            return;
        }

        Image combined = ComposePreview(map);
        _mapPreview.Texture = ImageTexture.CreateFromImage(combined);
        string mismatch = StringComparer.Ordinal.Equals(map.Hash, selection.MapHash)
            ? ""
            : ", local package differs from server";
        _mapStatus.Text = $"{map.DisplayName} - {map.Width}x{map.Height} - " +
                          $"suggested {map.SuggestedPlayers} players{mismatch}";
    }

    private void OnAdminChanged(bool isAdmin) => UpdateEditing(isAdmin);

    private void UpdateEditing(bool isAdmin)
    {
        bool canEdit = isAdmin && _selection != null;
        _modePicker.Disabled = !canEdit;
        _mapPicker.Disabled = !canEdit;
        _rulesSheet.SetEditable(canEdit);
        _physicsSheet.SetEditable(canEdit);
        _combatSheet.SetEditable(canEdit);
    }

    // BlendRect requires matching formats.
    public static Image ComposePreview(MapPackage map)
    {
        Image combined = (Image)map.Background.Duplicate();
        combined.Convert(Image.Format.Rgba8);
        Rect2I area = new(0, 0, map.Width, map.Height);
        combined.BlendRect(Rgba(map.Solid), area, Vector2I.Zero);
        combined.BlendRect(Rgba(map.Destructible), area, Vector2I.Zero);
        return combined;
    }

    // Package images are shared.
    private static Image Rgba(Image layer)
    {
        if (layer.GetFormat() == Image.Format.Rgba8)
            return layer;
        Image converted = (Image)layer.Duplicate();
        converted.Convert(Image.Format.Rgba8);
        return converted;
    }
}
