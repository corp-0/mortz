using Godot;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Menus.MainMenuAmbient;

public partial class MenuBackdrop : Control
{
    private static readonly ILogger _log = MortzLog.For("client");

    private const string BACKGROUND_SCENE_PATH =
        "res://official/Scenes/MainMenu/MainMenuBackground.tscn";

    private MainMenuBackground? _background;

    public bool HasAnimatedBackground => _background is not null;

    public override void _Ready()
    {
        if (!ResourceLoader.Exists(BACKGROUND_SCENE_PATH))
        {
            return;
        }

        PackedScene? scene = ResourceLoader.Load<PackedScene>(BACKGROUND_SCENE_PATH);
        if (scene is null)
        {
            _log.Warning("cannot load menu background from {Path}", BACKGROUND_SCENE_PATH);
            return;
        }

        Node instance = scene.Instantiate();
        if (instance is not MainMenuBackground background)
        {
            instance.Free();
            _log.Warning("menu background at {Path} has an invalid root", BACKGROUND_SCENE_PATH);
            return;
        }

        _background = background;
        AddChild(background);
    }

    public void StartSequence() => _background?.StartSequence();
}
