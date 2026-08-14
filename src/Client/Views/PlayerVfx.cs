using Godot;

namespace Mortz.Client.Views;

[GlobalClass]
public abstract partial class PlayerVfx : Node2D
{
    public abstract void Apply(in PlayerViewState state, in PlayerVisualPose pose);
}

public readonly record struct PlayerVisualPose(
    Texture2D BodyTexture,
    int BodyHframes,
    int BodyVframes,
    int BodyFrame,
    bool BodyFlipH,
    Texture2D LauncherTexture,
    Vector2 LauncherPosition,
    Vector2 LauncherScale,
    float AimRotation,
    bool LauncherFlipV,
    Vector2 Displacement,
    bool BodyVisible)
{
    public void ApplyBody(Sprite2D body)
    {
        body.Texture = BodyTexture;
        body.Hframes = BodyHframes;
        body.Vframes = BodyVframes;
        body.Frame = BodyFrame;
        body.FlipH = BodyFlipH;
        body.Visible = BodyVisible;
    }

    public void ApplyLauncher(Node2D aimPivot, Sprite2D launcher)
    {
        aimPivot.Rotation = AimRotation;
        aimPivot.Visible = BodyVisible;
        launcher.Texture = LauncherTexture;
        launcher.Position = LauncherPosition;
        launcher.Scale = LauncherScale;
        launcher.FlipV = LauncherFlipV;
    }
}
