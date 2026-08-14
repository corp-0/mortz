using Godot;

namespace Mortz.Client.Views;

[GlobalClass]
public partial class PlayerVfxPipeline : Node2D
{
    public void Apply(in PlayerViewState state, in PlayerVisualPose pose)
    {
        foreach (Node child in GetChildren())
        {
            if (child is PlayerVfx effect) effect.Apply(state, pose);
        }
    }
}
