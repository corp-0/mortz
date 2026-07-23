using Godot;

namespace Mortz.Client.Menus.MainMenuAmbient;

public partial class MenuBackdrop : Control
{
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
            GD.PushWarning($"Cannot load menu background from {BACKGROUND_SCENE_PATH}.");
            return;
        }

        Node instance = scene.Instantiate();
        if (instance is not MainMenuBackground background)
        {
            instance.Free();
            GD.PushWarning($"Menu background at {BACKGROUND_SCENE_PATH} has an invalid root.");
            return;
        }

        _background = background;
        AddChild(background);
    }

    public void StartSequence() => _background?.StartSequence();
}
