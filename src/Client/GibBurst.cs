using Godot;
using Mortz.Core;

namespace Mortz.Client;

/// <summary>
/// A player dying LieroX-style: a spray of blood pixels and a few meat chunks
/// flung from the body. Particles fly ballistically and collide with the
/// terrain mask; whatever lands paints a permanent stain through the callback
/// (GameMap's blood overlay). Purely client-side; frees itself.
/// </summary>
public partial class GibBurst : Node2D
{
    private const float LIFETIME = 3.5f;
    private const int BLOOD_COUNT = 420;
    private const int CHUNK_COUNT = 24;

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color;
        public float Size;
    }

    private readonly List<Particle> _particles = new();
    private TerrainMask _mask = null!;
    private Action<int, int, Color> _paint = null!;
    private float _age;
    public float PlaybackSpeed { get; set; } = 1f;

    public static GibBurst Create(Vector2 center, TerrainMask mask, Action<int, int, Color> paint)
    {
        GibBurst burst = new GibBurst { _mask = mask, _paint = paint };
        Random rng = new Random();

        for (int i = 0; i < BLOOD_COUNT; i++)
        {
            Vector2 dir = Vector2.FromAngle((float)(rng.NextDouble() * Math.Tau));
            float shade = 0.55f + (float)rng.NextDouble() * 0.4f;
            burst._particles.Add(new Particle
            {
                Position = center + dir * rng.Next(12),
                Velocity = dir * (60 + rng.Next(520)) + new Vector2(0, -60 - rng.Next(160)),
                Color = new Color(shade, 0.02f, 0.02f),
                Size = 1 + rng.Next(3),
            });
        }
        for (int i = 0; i < CHUNK_COUNT; i++)
        {
            Vector2 dir = Vector2.FromAngle((float)(rng.NextDouble() * Math.Tau));
            burst._particles.Add(new Particle
            {
                Position = center,
                Velocity = dir * (40 + rng.Next(280)) + new Vector2(0, -140),
                Color = new Color(0.3f + (float)rng.NextDouble() * 0.25f, 0.04f, 0.04f),
                Size = 3 + rng.Next(4),
            });
        }
        return burst;
    }

    public override void _Process(double delta)
    {
        delta *= PlaybackSpeed;
        _age += (float)delta;
        if (_age >= LIFETIME || _particles.Count == 0)
        {
            QueueFree();
            return;
        }
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            Particle p = _particles[i];
            p.Velocity += new Vector2(0, 900f * (float)delta);
            p.Position += p.Velocity * (float)delta;
            if (_mask.IsSolid((int)p.Position.X, (int)p.Position.Y))
            {
                // Landed: stain the terrain and stop existing. Bigger pieces
                // smear wider splats. Only solid cells take the stain, so a
                // carve can always erase it; painted on air it would float.
                int splat = (int)p.Size / 2;
                for (int dy = -splat; dy <= splat; dy++)
                    for (int dx = -splat; dx <= splat; dx++)
                    {
                        int sx = (int)p.Position.X + dx, sy = (int)p.Position.Y + dy;
                        if (_mask.IsSolid(sx, sy))
                            _paint(sx, sy, p.Color);
                    }
                _particles.RemoveAt(i);
                continue;
            }
            _particles[i] = p;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Particle p in _particles)
            DrawRect(new Rect2(p.Position, new Vector2(p.Size, p.Size)), p.Color);
    }
}
