using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Settings;
using Mortz.Core.Sim;

namespace Mortz.Client.Menus;

/// <summary>Edits the local player's identity.</summary>
[Meta(typeof(IAutoNode))]
public partial class SettingsScreen : Control
{
    [Signal] public delegate void BackRequestedEventHandler();
    [Signal] public delegate void SavedEventHandler();

    [Export] private LineEdit _playerNameEdit = null!;
    [Export] private TextureRect _skinPreview = null!;
    [Export] private Label _skinPosition = null!;
    [Export] private Texture2D _skinSheet = null!;
    [Export] private Label _status = null!;

    private int _selectedSkin;

    [Dependency]
    private ClientSettings Settings => this.DependOn<ClientSettings>();

    public override void _Notification(int what) => this.Notify(what);

    public void Open(string prompt = "")
    {
        _playerNameEdit.Text = Settings.PlayerName;
        _selectedSkin = Settings.Skin;
        UpdateSkinPreview();
        _status.Text = prompt;
        _playerNameEdit.GrabFocus();
    }

    // ---- button handlers (connected in Settings.tscn) ----

    public void OnSavePressed()
    {
        Settings.SetIdentity(_playerNameEdit.Text, _selectedSkin);
        _playerNameEdit.Text = Settings.PlayerName;
        if (!Settings.HasIdentity)
        {
            _status.Text = "Choose a name and skin before playing.";
            return;
        }
        EmitSignal(SignalName.Saved);
    }

    public void OnPreviousSkinPressed() => SelectSkin(_selectedSkin - 1);

    public void OnNextSkinPressed() => SelectSkin(_selectedSkin + 1);

    public void OnBackPressed() => EmitSignal(SignalName.BackRequested);

    private void SelectSkin(int skin)
    {
        _selectedSkin = (skin + SimConfig.SKIN_COUNT) % SimConfig.SKIN_COUNT;
        UpdateSkinPreview();
    }

    private void UpdateSkinPreview()
    {
        int frameWidth = _skinSheet.GetWidth() / SimConfig.SKIN_COLUMNS;
        int rows = (SimConfig.SKIN_COUNT + SimConfig.SKIN_COLUMNS - 1) / SimConfig.SKIN_COLUMNS;
        int frameHeight = _skinSheet.GetHeight() / rows;
        int column = _selectedSkin % SimConfig.SKIN_COLUMNS;
        int row = _selectedSkin / SimConfig.SKIN_COLUMNS;
        _skinPreview.Texture = new AtlasTexture
        {
            Atlas = _skinSheet,
            Region = new Rect2(column * frameWidth, row * frameHeight,
                frameWidth, frameHeight),
        };
        _skinPosition.Text = $"{_selectedSkin + 1} / {SimConfig.SKIN_COUNT}";
    }
}
