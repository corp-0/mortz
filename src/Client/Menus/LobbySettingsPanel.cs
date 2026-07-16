using System.Text;
using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Chat;
using Mortz.Core.Admin;
using Mortz.Core.Match;
using Mortz.Core.Net;
using Mortz.Core.Net.Messages;
using Mortz.Core.Ui;
using Mortz.Shared;

namespace Mortz.Client.Menus;

/// <summary>
/// Lobby-owned presentation of the canonical server setup. Categories and
/// rule bindings come from generated metadata; concrete value types select a
/// reusable control prefab.
/// </summary>
[Meta(typeof(IAutoNode))]
public partial class LobbySettingsPanel : PanelContainer
{
    internal const int CATEGORY_GAP = 22;

    [Export] private Label _adminStatus = null!;
    [Export] private OptionButton _mapPicker = null!;
    [Export] private TextureRect _mapPreview = null!;
    [Export] private Label _mapStatus = null!;
    [Export] private VBoxContainer _rules = null!;
    [Export] private PackedScene _boolControl = null!;
    [Export] private PackedScene _intControl = null!;
    [Export] private PackedScene _floatControl = null!;
    [Export] private PackedScene _enumControl = null!;

    private readonly List<IMatchRuleControl> _controls = [];
    private readonly List<string> _mapIds = [];
    private MatchConfig _config = new();
    private bool _applyingState;
    private bool _hasServerState;
    private bool _subscribed;
    private string _previewMapId = "";
    private string _previewMapHash = "";

    [Dependency]
    public IClientChat Chat => this.DependOn<IClientChat>();

    internal int RuleControlCount => _controls.Count;
    internal int CategoryBlockCount { get; private set; }
    internal float RulesMinimumHeight => ((ScrollContainer)_rules.GetParent()).CustomMinimumSize.Y;

    public override void _Notification(int what) => this.Notify(what);

    public void OnReady()
    {
        _mapPicker.ItemSelected += OnMapSelected;
        BuildRules();
        UpdateEditing(isAdmin: false);
    }

    public void OnResolved()
    {
        LobbySettingsMsg.Received += OnSettings;
        LobbyStateMsg.Received += OnLobbyState;
        Chat.AdminChanged += OnAdminChanged;
        _subscribed = true;
        UpdateEditing(Chat.IsAdmin);
    }

    public void OnExitTree()
    {
        _mapPicker.ItemSelected -= OnMapSelected;
        if (!_subscribed)
            return;
        LobbySettingsMsg.Received -= OnSettings;
        LobbyStateMsg.Received -= OnLobbyState;
        Chat.AdminChanged -= OnAdminChanged;
        _subscribed = false;
    }

    private void OnSettings(LobbySettingsMsg message)
    {
        if (message.MapIds.Length != message.MapNames.Length ||
            message.MapIds.Length > NetConfig.MAX_LOBBY_MAPS)
        {
            _mapStatus.Text = "Server sent an invalid map catalog.";
            return;
        }

        MatchConfig config;
        try
        {
            config = MatchConfig.FromBytes(message.Config);
        }
        catch (IOException)
        {
            _mapStatus.Text = "Server sent invalid match settings.";
            return;
        }

        _config = config;
        _hasServerState = true;
        ApplyMapOptions(message.MapId, message.MapIds, message.MapNames);
        foreach (IMatchRuleControl control in _controls)
            control.UpdateConfig(_config);
        UpdateEditing(Chat.IsAdmin);
        UpdatePreview(message.MapId, message.MapHash);
    }

    internal void ApplySettingsForTest(LobbySettingsMsg message) => OnSettings(message);

    private void ApplyMapOptions(string selectedMap, string[] mapIds, string[] mapNames)
    {
        _applyingState = true;
        _mapPicker.Clear();
        _mapIds.Clear();
        int selected = -1;
        for (int i = 0; i < mapIds.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(mapIds[i]))
                continue;
            if (mapIds[i] == selectedMap)
                selected = _mapIds.Count;
            _mapIds.Add(mapIds[i]);
            _mapPicker.AddItem(string.IsNullOrWhiteSpace(mapNames[i]) ? mapIds[i] : mapNames[i]);
        }
        if (selected >= 0)
            _mapPicker.Select(selected);
        _applyingState = false;
    }

    private void BuildRules()
    {
        int categoryIndex = 0;
        foreach (UiCategoryDescriptor<MatchConfig> category in MatchConfigUiMetadata.Categories)
        {
            MarginContainer categoryMargin = new();
            categoryMargin.AddThemeConstantOverride(
                "margin_top", categoryIndex == 0 ? 0 : CATEGORY_GAP);
            categoryMargin.AddThemeConstantOverride("margin_bottom", 6);
            VBoxContainer categoryBlock = new();
            categoryBlock.AddThemeConstantOverride("separation", 7);
            categoryMargin.AddChild(categoryBlock);

            Label heading = new() { Text = category.DisplayName };
            heading.AddThemeFontSizeOverride("font_size", 18);
            heading.AddThemeColorOverride("font_color", new Color("cbd5e1"));
            categoryBlock.AddChild(heading);
            categoryBlock.AddChild(new HSeparator());
            foreach (IUiPropertyDescriptor<MatchConfig> descriptor in category.Properties)
            {
                PackedScene? scene = ControlScene(descriptor.ValueType);
                if (scene == null)
                {
                    categoryBlock.AddChild(new Label
                    {
                        Text = $"{descriptor.DisplayName}: unsupported {descriptor.ValueType.Name}",
                    });
                    continue;
                }
                Node node = scene.Instantiate();
                if (node is not IMatchRuleControl control)
                {
                    node.Free();
                    continue;
                }
                control.Bind(descriptor, _config, OnRuleChanged);
                _controls.Add(control);
                categoryBlock.AddChild(node);
            }

            _rules.AddChild(categoryMargin);
            categoryIndex++;
        }
        CategoryBlockCount = categoryIndex;
    }

    private PackedScene? ControlScene(Type valueType)
    {
        if (valueType == typeof(bool))
            return _boolControl;
        if (valueType == typeof(int))
            return _intControl;
        if (valueType == typeof(float))
            return _floatControl;
        return valueType.IsEnum ? _enumControl : null;
    }

    private void OnRuleChanged()
    {
        if (!Chat.IsAdmin)
            return;
        byte[] payload = _config.ToBytes();
        if (Chat.TrySignAdminAction(AdminAction.SET_LOBBY_RULES, payload,
                out ulong sequence, out byte[] tag))
        {
            new LobbyRulesUpdateMsg(payload, sequence, tag).SendToServer();
        }
    }

    private void OnMapSelected(long index)
    {
        if (_applyingState || !Chat.IsAdmin || index < 0 || index >= _mapIds.Count)
            return;
        string mapId = _mapIds[(int)index];
        byte[] payload = Encoding.UTF8.GetBytes(mapId);
        if (Chat.TrySignAdminAction(AdminAction.SET_LOBBY_MAP, payload,
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
        MapPackage? map = MapPackage.Load(mapId);
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

    private static void OnLobbyState(LobbyStateMsg message) =>
        new LobbySettingsRequestMsg().SendToServer();

    private void UpdateEditing(bool isAdmin)
    {
        bool canEdit = isAdmin && _hasServerState;
        _adminStatus.Text = !_hasServerState
            ? "Loading server setup..."
            : isAdmin
            ? "Admin controls enabled"
            : "Read-only, use /admin <password> in chat to edit";
        _adminStatus.Modulate = canEdit
            ? new Color("86efac")
            : new Color("94a3b8");
        _mapPicker.Disabled = !canEdit;
        foreach (IMatchRuleControl control in _controls)
            control.SetEditable(canEdit);
    }

    internal static Image ComposePreview(MapPackage map)
    {
        Image combined = (Image)map.Background.Duplicate();
        Rect2I area = new(0, 0, map.Width, map.Height);
        combined.BlendRect(map.Solid, area, Vector2I.Zero);
        combined.BlendRect(map.Destructible, area, Vector2I.Zero);
        return combined;
    }
}
