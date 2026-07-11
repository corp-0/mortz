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
