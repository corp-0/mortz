using Godot;

namespace Mortz.Client;

/// <summary>
/// Placeholder shell visual until the real prefab exists: a dark disc with a
/// nose line. Drawn pointing +X; GameView rotates it along the velocity.
/// </summary>
public partial class MortarView : Node2D
{
    private static readonly Color _body = new(0.15f, 0.15f, 0.15f);
    private static readonly Color _nose = new(0.95f, 0.6f, 0.2f);

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, 8, _body);
        DrawLine(Vector2.Zero, new Vector2(13, 0), _nose, 3);
    }
}
