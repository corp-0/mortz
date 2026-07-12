using Godot;

namespace Mortz.Client;

/// <summary>
/// One-shot cosmetic debris burst: a handful of pixels flung from a carve,
/// colored from the destroyed terrain pixels. Purely client-side; frees itself.
/// </summary>
public partial class CarveBurst : Node2D
{
    private const float LIFETIME = 0.6f;
    private const int MAX_PARTICLES = 40;

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color;
    }

    private readonly List<Particle> _particles = new();
    private float _age;

    private static readonly Color[] _fire =
    {
        new(1f, 0.92f, 0.45f),  // flash yellow
        new(1f, 0.62f, 0.12f),  // orange
        new(0.88f, 0.3f, 0.05f), // ember red
        new(0.35f, 0.32f, 0.3f), // smoke grey
    };

    public static CarveBurst Create(Vector2 center, IReadOnlyList<(Vector2 Position, Color Color)> pixels)
    {
        CarveBurst burst = new CarveBurst();
        Random rng = new Random();
        int stride = Math.Max(1, pixels.Count / MAX_PARTICLES);
        for (int i = 0; i < pixels.Count; i += stride)
        {
            Vector2 away = (pixels[i].Position - center).Normalized();
            burst._particles.Add(new Particle
            {
                Position = pixels[i].Position,
                Velocity = away * (60 + rng.Next(160)) + new Vector2(0, -80 - rng.Next(80)),
                Color = pixels[i].Color,
            });
        }
        return burst;
    }

    /// <summary>The boom itself: fire and smoke flung radially, scaled to the
    /// blast radius. Spawns on every explosion, carve or not.</summary>
    public static CarveBurst Explosion(Vector2 center, int radius)
    {
        CarveBurst burst = new CarveBurst();
        Random rng = new Random();
        for (int i = 0; i < MAX_PARTICLES; i++)
        {
            Vector2 dir = Vector2.FromAngle((float)(rng.NextDouble() * Math.Tau));
            burst._particles.Add(new Particle
            {
                Position = center + dir * rng.Next(radius / 2),
                Velocity = dir * (radius * 3 + rng.Next(radius * 6)) + new Vector2(0, -60),
                Color = _fire[rng.Next(_fire.Length)],
            });
        }
        return burst;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= LIFETIME)
        {
            QueueFree();
            return;
        }
        Span<Particle> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_particles);
        for (int i = 0; i < span.Length; i++)
        {
            span[i].Velocity += new Vector2(0, 900f * (float)delta);
            span[i].Position += span[i].Velocity * (float)delta;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float alpha = 1f - _age / LIFETIME;
        foreach (Particle p in _particles)
            DrawRect(new Rect2(p.Position, new Vector2(2, 2)), p.Color with { A = alpha });
    }
}
