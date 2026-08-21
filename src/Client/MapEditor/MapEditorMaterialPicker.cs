using Godot;

namespace Mortz.Client.MapEditor;

[GlobalClass]
public partial class MapEditorMaterialPicker : VBoxContainer
{
    private readonly List<ImageTexture> _previews = [];
    private readonly ButtonGroup _textureButtons = new();
    private readonly List<MapEditorTextureCatalogItem> _catalog = [];
    [Export] private OptionButton _kind = null!;
    [Export] private VBoxContainer _texturePanel = null!;
    [Export] private LineEdit _search = null!;
    [Export] private GridContainer _textureList = null!;
    [Export] private Label _empty = null!;
    [Export] private VBoxContainer _colorPanel = null!;
    [Export] private ColorPickerButton _color = null!;
    private MapEditorTextureSourceRegistry _sources = new();
    private MapEditorTextureReference? _lastTexture;

    private MapEditorBrushMaterial _material =
        new MapEditorSolidColorMaterial(new MapEditorColor(255, 255, 255));

    private bool _applying;

    public event Action<MapEditorBrushMaterial>? MaterialSelected;

    public MapEditorBrushMaterial SelectedMaterial => _material;

    public override void _Ready()
    {
        _kind.ItemSelected += SelectKind;
        _search.TextChanged += _ => RebuildTextureList();
        _color.PopupClosed += CommitColor;

        RebuildTextureList();
        UpdateKindVisibility();
    }

    public override void _ExitTree()
    {
        DisposePreviews();
    }

    public void Configure(MapEditorTextureSourceRegistry sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _catalog.Clear();
        _catalog.AddRange(_sources.DiscoverTextures());
        if (IsNodeReady())
        {
            RebuildTextureList();
        }
    }

    public void Apply(MapEditorBrushMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        _applying = true;
        _material = material;
        switch (material)
        {
            case MapEditorTextureMaterial texture:
                _lastTexture = texture.Reference;
                _kind.Select(0);
                break;
            case MapEditorSolidColorMaterial solid:
                _kind.Select(1);
                _color.Color = ToGodot(solid.Color);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(material));
        }

        UpdateKindVisibility();
        UpdateTextureSelection();
        _applying = false;
    }

    private void SelectKind(long index)
    {
        UpdateKindVisibility();
        if (_applying)
            return;
        if (index == 1)
        {
            Select(new MapEditorSolidColorMaterial(FromGodot(_color.Color)));
        }
        else
        {
            MapEditorTextureReference? texture = _lastTexture ?? _catalog.FirstOrDefault()?.Reference;
            if (texture != null)
            {
                _lastTexture = texture;
                Select(new MapEditorTextureMaterial(texture));
            }
            else
            {
                _kind.Select(1);
                UpdateKindVisibility();
            }
        }
    }

    private void CommitColor()
    {
        if (!_applying && _kind.Selected == 1)
            Select(new MapEditorSolidColorMaterial(FromGodot(_color.Color)));
    }

    private void SelectTexture(MapEditorTextureReference reference)
    {
        _lastTexture = reference;
        Select(new MapEditorTextureMaterial(reference));
    }

    private void Select(MapEditorBrushMaterial material)
    {
        _material = material;
        UpdateTextureSelection();
        MaterialSelected?.Invoke(material);
    }

    private void UpdateKindVisibility()
    {
        _texturePanel.Visible = _kind.Selected == 0;
        _colorPanel.Visible = _kind.Selected == 1;
    }

    private void RebuildTextureList()
    {
        DisposePreviews();
        foreach (Node child in _textureList.GetChildren())
        {
            child.Free();
        }

        string filter = _search.Text.Trim();
        int visible = 0;
        foreach (MapEditorTextureCatalogItem item in _catalog)
        {
            string text = $"{item.SourceName} * {item.Name}";
            if (filter.Length > 0 && !text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            Button button = new()
            {
                Name = $"Texture{visible}",
                TooltipText = $"{item.SourceName}\n{item.Name}\n{item.Reference.Location}",
                AccessibilityName = $"Use texture {item.Name} from {item.SourceName}",
                ToggleMode = true,
                ButtonGroup = _textureButtons,
                CustomMinimumSize = new Vector2(72, 72),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ExpandIcon = true,
            };
            MapEditorTextureLoadResult preview = _sources.Load(item.Reference);
            if (preview.Status == MapEditorTextureLoadStatus.RESOLVED && preview.Texture != null)
            {
                ImageTexture texture = CreateTexture(preview.Texture);
                _previews.Add(texture);
                button.Icon = texture;
            }

            button.Pressed += () => SelectTexture(item.Reference);
            button.SetMeta("material_location", item.Reference.Location);
            _textureList.AddChild(button);
            visible++;
        }

        _empty.Visible = visible == 0;
        UpdateTextureSelection();
    }

    private void UpdateTextureSelection()
    {
        foreach (Node child in _textureList.GetChildren())
        {
            if (child is Button button)
            {
                bool selected = _material is MapEditorTextureMaterial texture &&
                                button.GetMeta("material_location").AsString() ==
                                texture.Reference.Location;
                button.SetPressedNoSignal(selected);
            }
        }
    }

    private void DisposePreviews()
    {
        foreach (ImageTexture preview in _previews)
        {
            preview.Dispose();
        }

        _previews.Clear();
    }

    private static ImageTexture CreateTexture(MapEditorTextureData data)
    {
        using Image image = Image.CreateFromData(data.Width, data.Height, false,
            Image.Format.Rgba8, data.Rgba.ToArray());
        const int PREVIEW_SIZE = 44;
        if (image.GetWidth() > PREVIEW_SIZE || image.GetHeight() > PREVIEW_SIZE)
        {
            float scale = Math.Min(PREVIEW_SIZE / (float)image.GetWidth(),
                PREVIEW_SIZE / (float)image.GetHeight());
            image.Resize(Math.Max(1, (int)MathF.Round(image.GetWidth() * scale)),
                Math.Max(1, (int)MathF.Round(image.GetHeight() * scale)),
                Image.Interpolation.Nearest);
        }

        return ImageTexture.CreateFromImage(image);
    }

    private static Color ToGodot(MapEditorColor color) => Color.Color8(
        color.Red, color.Green, color.Blue, color.Alpha);

    private static MapEditorColor FromGodot(Color color) => new(
        (byte)Math.Clamp((int)MathF.Round(color.R * 255), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.G * 255), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.B * 255), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.A * 255), 0, 255));
}
