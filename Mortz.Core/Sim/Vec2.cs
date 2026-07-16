namespace Mortz.Core.Sim;

/// <summary>
/// Our own small 2D vector, so Mortz.Core never has to reference GodotSharp.
/// The Godot shell converts to Vector2 at the edge.
/// </summary>
public readonly record struct Vec2(float X, float Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(float s, Vec2 a) => a * s;
    public static Vec2 operator /(Vec2 a, float s) => new(a.X / s, a.Y / s);

    public float Length() => MathF.Sqrt(X * X + Y * Y);
    public float LengthSquared() => X * X + Y * Y;

    public Vec2 Normalized()
    {
        float len = Length();
        return len > 1e-6f ? this / len : Zero;
    }

    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
