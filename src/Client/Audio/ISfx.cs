using Godot;

namespace Mortz.Client.Audio;

public interface ISfx
{
    SoundRegistry Sounds { get; }

    SfxHandle Play(SoundEffect? sound, float pitch = 1f, float gainDb = 0f);

    SfxHandle PlayAt(SoundEffect? sound, Vector2 position, float pitch = 1f, float gainDb = 0f);

    SfxHandle PlayAttached(SoundEffect? sound, Node2D target, float pitch = 1f, float gainDb = 0f);
}
