using Godot;

namespace Mortz.Client.Audio;

public sealed class NullSfx : ISfx
{
    private SoundRegistry? _sounds;

    public SoundRegistry Sounds => _sounds ??= new SoundRegistry();

    public SfxHandle Play(SoundEffect? sound, float pitch = 1f, float gainDb = 0f) => default;

    public SfxHandle PlayAt(SoundEffect? sound, Vector2 position, float pitch = 1f,
        float gainDb = 0f) => default;

    public SfxHandle PlayAttached(SoundEffect? sound, Node2D target, float pitch = 1f,
        float gainDb = 0f) => default;
}
