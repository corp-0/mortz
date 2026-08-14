using Godot;
using Mortz.Core.Match.Events;

namespace Mortz.Client.Views;

[GlobalClass]
public partial class KillingSpreeAura : PlayerVfx
{
    private static readonly VisualTier[] _visualTiers =
    [
        new(2, 0.10f, 0.18f, 0.46f, 0.38f, 0.95f, 0.10f),
        new(3, 0.085f, 0.22f, 0.56f, 0.35f, 0.85f, 0.12f),
        new(3, 0.07f, 0.25f, 0.68f, 0.32f, 0.75f, 0.14f),
        new(4, 0.055f, 0.30f, 0.76f, 0.29f, 0.60f, 0.16f),
        new(5, 0.04f, 0.36f, 0.84f, 0.26f, 0.45f, 0.18f),
    ];

    [Export] private Shader _afterimageShader = null!;
    [Export] private Sprite2D _bodyGlint = null!;
    [Export] private Node2D _aimPivot = null!;
    [Export] private Sprite2D _launcherGlint = null!;

    [Export(PropertyHint.Range, "0.02,0.2,0.005")]
    private float _trailInterval = 0.07f;

    [Export(PropertyHint.Range, "0.05,1.0,0.01")]
    private float _trailLifetime = 0.25f;

    [Export(PropertyHint.Range, "1,8,1")]
    private int _maxAfterimages = 3;

    [Export(PropertyHint.Range, "0.0,5.0,0.1")]
    private float _minimumMovement = 0.75f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    private float _trailOpacity = 0.68f;

    [Export] private Color _blueShadow = new(0.12f, 0.28f, 0.85f);
    [Export] private Color _blueHighlight = new(0.38f, 0.95f, 1f);
    [Export] private Color _violetShadow = new(0.45f, 0.12f, 0.85f);
    [Export] private Color _violetHighlight = new(1f, 0.42f, 1f);

    private readonly List<Afterimage> _afterimages = [];
    private bool _active;
    private float _sinceAfterimage = float.MaxValue;
    private int _paletteIndex;
    private int _magnitude = -1;
    private ShaderMaterial _bodyGlintMaterial = null!;
    private ShaderMaterial _launcherGlintMaterial = null!;

    public bool Active => _active;

    public override void _Ready()
    {
        _bodyGlintMaterial = (ShaderMaterial)_bodyGlint.Material;
        _launcherGlintMaterial = (ShaderMaterial)_launcherGlint.Material;
        Visible = false;
        SetProcess(false);
    }

    public void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;
        Visible = active;
        SetProcess(active);
        if (!active)
        {
            ClearAfterimages();
        }
    }

    public void SetMagnitude(int magnitude)
    {
        if (_magnitude == magnitude)
        {
            return;
        }

        _magnitude = magnitude;
        if (magnitude < Streaks.ANNOUNCEMENT_ENTRY)
        {
            SetActive(false);
            return;
        }

        int tierIndex = Math.Clamp(
            (magnitude - Streaks.ANNOUNCEMENT_ENTRY) / 2,
            0,
            _visualTiers.Length - 1);
        ApplyTier(_visualTiers[tierIndex]);
        SetActive(true);
    }

    public override void Apply(in PlayerViewState state, in PlayerVisualPose pose)
    {
        SetMagnitude(state.Presentation.KillingSpreeMagnitude);
        pose.ApplyBody(_bodyGlint);
        pose.ApplyLauncher(_aimPivot, _launcherGlint);

        foreach (Afterimage afterimage in _afterimages)
        {
            afterimage.Root.Position -= pose.Displacement;
        }

        if (!Active || !pose.BodyVisible || _sinceAfterimage < _trailInterval ||
            pose.Displacement.LengthSquared() < _minimumMovement * _minimumMovement)
        {
            return;
        }

        AddAfterimage(pose);
        _sinceAfterimage = 0f;
    }

    public override void _Process(double delta)
    {
        _sinceAfterimage += (float)delta;

        for (int i = _afterimages.Count - 1; i >= 0; i--)
        {
            Afterimage afterimage = _afterimages[i];
            afterimage.Age += (float)delta;
            float progress = afterimage.Age / _trailLifetime;
            if (progress >= 1f)
            {
                RemoveAfterimage(i);
                continue;
            }

            afterimage.Material.SetShaderParameter(
                "opacity", _trailOpacity * (1f - progress));
        }
    }

    private void ApplyTier(in VisualTier tier)
    {
        _maxAfterimages = tier.MaxAfterimages;
        _trailInterval = tier.TrailInterval;
        _trailLifetime = tier.TrailLifetime;
        _trailOpacity = tier.TrailOpacity;
        while (_afterimages.Count > _maxAfterimages)
        {
            RemoveAfterimage(0);
        }

        SetGlintParameter("sweep_duration", tier.GlintDuration);
        SetGlintParameter("pause_duration", tier.GlintPause);
        SetGlintParameter("shine_size", tier.GlintSize);
    }

    private void SetGlintParameter(StringName parameter, float value)
    {
        _bodyGlintMaterial.SetShaderParameter(parameter, value);
        _launcherGlintMaterial.SetShaderParameter(parameter, value);
    }

    private void AddAfterimage(in PlayerVisualPose pose)
    {
        while (_afterimages.Count >= _maxAfterimages)
        {
            RemoveAfterimage(0);
        }

        bool useBlue = _paletteIndex++ % 2 == 0;
        ShaderMaterial material = new()
        {
            Shader = _afterimageShader,
        };
        material.SetShaderParameter("shadow_color", useBlue ? _blueShadow : _violetShadow);
        material.SetShaderParameter("highlight_color", useBlue ? _blueHighlight : _violetHighlight);
        material.SetShaderParameter("opacity", _trailOpacity);

        Node2D root = new()
        {
            ZIndex = -1,
        };
        Sprite2D body = new()
        {
            Texture = pose.BodyTexture,
            Hframes = pose.BodyHframes,
            Vframes = pose.BodyVframes,
            Frame = pose.BodyFrame,
            FlipH = pose.BodyFlipH,
            Material = material,
        };

        Node2D aimPivot = new()
        {
            Rotation = pose.AimRotation,
        };
        Sprite2D launcher = new()
        {
            Texture = pose.LauncherTexture,
            Position = pose.LauncherPosition,
            Scale = pose.LauncherScale,
            FlipV = pose.LauncherFlipV,
            Material = material,
        };

        AddChild(root);
        root.AddChild(body);
        root.AddChild(aimPivot);
        aimPivot.AddChild(launcher);
        _afterimages.Add(new Afterimage(root, material));
    }

    private void ClearAfterimages()
    {
        for (int i = _afterimages.Count - 1; i >= 0; i--)
        {
            RemoveAfterimage(i);
        }
    }

    private void RemoveAfterimage(int index)
    {
        _afterimages[index].Root.QueueFree();
        _afterimages.RemoveAt(index);
    }

    private sealed class Afterimage(Node2D root, ShaderMaterial material)
    {
        public Node2D Root { get; } = root;
        public ShaderMaterial Material { get; } = material;
        public float Age { get; set; }
    }

    private readonly record struct VisualTier(
        int MaxAfterimages,
        float TrailInterval,
        float TrailLifetime,
        float TrailOpacity,
        float GlintDuration,
        float GlintPause,
        float GlintSize);
}
