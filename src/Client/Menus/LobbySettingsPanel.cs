using System.Text;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Admin;
using Mortz.Client.Setup;
using Mortz.Client.Ui;
using Mortz.Core.Admin;
using Mortz.Core.Match;
using Mortz.Core.Net.Messages;
using Mortz.Shared;

namespace Mortz.Client.Menus;

/// <summary>
/// Lobby-owned presentation of the canonical server setup. State lives in
/// MatchSetup; this panel renders it and pushes signed admin edits from its
/// own editing copy.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class LobbySettingsPanel : PanelContainer
{
    [Export] private OptionButton _modePicker = null!;
    [Export] private OptionButton _mapPicker = null!;
    [Export] private TextureRect _mapPreview = null!;
    [Export] private Label _mapStatus = null!;
    [Export] private UiPropertySheet _sheet = null!;

    private readonly List<string> _mapIds = [];
    private readonly List<string> _modeIds = [];
    private MatchConfig _config = new();
    private bool _applyingState;
    // Snapshot of Setup.HasServerState; UpdateEditing also runs before the
    // dependency resolves.
    private bool _hasServerState;
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
        _sheet.Build(MatchConfigUiMetadata.Categories, _config, OnRuleChanged);
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
        _hasServerState = Setup.HasServerState;
        if (Setup.SettingsError != "")
        {
            _mapStatus.Text = Setup.SettingsError;
            return;
        }
        if (!_hasServerState)
        {
            UpdateEditing(Admin.IsAdmin);
            return;
        }

        _config = Setup.CopyRules();
        ApplyMapOptions(Setup.MapId, Setup.MapOptions);
        ApplyModeOptions(Setup.ModeId, Setup.ModeOptions);
        _sheet.UpdateModel(_config);
        UpdateEditing(Admin.IsAdmin);
        UpdatePreview(Setup.MapId, Setup.MapHash);
    }

    private void ApplyMapOptions(string selectedMap, IReadOnlyList<ContentOption> options)
    {
        _applyingState = true;
        _mapPicker.Clear();
        _mapIds.Clear();
        int selected = -1;
        foreach (ContentOption option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Id))
                continue;
            if (option.Id == selectedMap)
                selected = _mapIds.Count;
            _mapIds.Add(option.Id);
            _mapPicker.AddItem(string.IsNullOrWhiteSpace(option.Name) ? option.Id : option.Name);
        }
        if (selected >= 0)
            _mapPicker.Select(selected);
        _applyingState = false;
    }

    /// <summary>Adds a trailing "Custom" entry, picked when the rules match no mode.</summary>
    private void ApplyModeOptions(string selectedMode, IReadOnlyList<ContentOption> options)
    {
        _applyingState = true;
        _modePicker.Clear();
        _modeIds.Clear();
        int selected = -1;
        foreach (ContentOption option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Id))
                continue;
            if (option.Id == selectedMode)
                selected = _modeIds.Count;
            _modeIds.Add(option.Id);
            _modePicker.AddItem(string.IsNullOrWhiteSpace(option.Name) ? option.Id : option.Name);
        }
        _modePicker.AddItem("Custom");
        _modePicker.Select(selected >= 0 ? selected : _modeIds.Count);
        _applyingState = false;
    }

    private void OnModeSelected(long index)
    {
        if (_applyingState || !Admin.IsAdmin || index < 0)
            return;
        if (index >= _modeIds.Count)
        {
            // Nothing to apply for "Custom", snap back to the real state.
            ApplyModeOptions(Setup.ModeId, Setup.ModeOptions);
            return;
        }
        string modeId = _modeIds[(int)index];
        byte[] payload = Encoding.UTF8.GetBytes(modeId);
        if (Admin.TrySignAdminAction(AdminAction.SET_LOBBY_MODE, payload,
                out ulong sequence, out byte[] tag))
        {
            new LobbyModeUpdateMsg(modeId, sequence, tag).SendToServer();
        }
    }

    private void OnRuleChanged()
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

    private void UpdatePreview(string mapId, string mapHash)
    {
        if (_previewMapId == mapId && _previewMapHash == mapHash)
            return;
        _previewMapId = mapId;
        _previewMapHash = mapHash;
        MapPackage? map = GameContent.Load()?.LoadMap(mapId);
        if (map == null)
        {
            _mapPreview.Texture = null;
            _mapStatus.Text = $"Map '{mapId}' is not installed locally; preview unavailable.";
            return;
        }

        Image combined = ComposePreview(map);
        _mapPreview.Texture = ImageTexture.CreateFromImage(combined);
        string mismatch = StringComparer.Ordinal.Equals(map.Hash, mapHash)
            ? ""
            : ", local package differs from server";
        _mapStatus.Text = $"{map.DisplayName} - {map.Width}x{map.Height} - " +
                          $"suggested {map.SuggestedPlayers} players{mismatch}";
    }

    private void OnAdminChanged(bool isAdmin) => UpdateEditing(isAdmin);

    private void UpdateEditing(bool isAdmin)
    {
        bool canEdit = isAdmin && _hasServerState;
        _modePicker.Disabled = !canEdit;
        _mapPicker.Disabled = !canEdit;
        _sheet.SetEditable(canEdit);
    }

    // Layer PNGs decode to whatever format they were saved in (an RGB
    // background without alpha is legal, and user maps will bring anything),
    // while BlendRect refuses mismatched formats. Normalize to RGBA first.
    internal static Image ComposePreview(MapPackage map)
    {
        Image combined = (Image)map.Background.Duplicate();
        combined.Convert(Image.Format.Rgba8);
        Rect2I area = new(0, 0, map.Width, map.Height);
        combined.BlendRect(Rgba(map.Solid), area, Vector2I.Zero);
        combined.BlendRect(Rgba(map.Destructible), area, Vector2I.Zero);
        return combined;
    }

    /// <summary>Copies only when a conversion is actually needed; the
    /// package's images are shared and must not be mutated.</summary>
    private static Image Rgba(Image layer)
    {
        if (layer.GetFormat() == Image.Format.Rgba8)
            return layer;
        Image converted = (Image)layer.Duplicate();
        converted.Convert(Image.Format.Rgba8);
        return converted;
    }
}
