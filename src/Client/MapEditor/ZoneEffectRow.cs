using Godot;
using Mortz.Content;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Client.MapEditor;

public partial class ZoneEffectRow : VBoxContainer
{
    [Export] private OptionButton _stat = null!;
    [Export] private OptionButton _op = null!;
    [Export] private SpinBox _value = null!;

    public event Action? Changed;
    public event Action<ZoneEffectRow>? RemoveRequested;

    public override void _Ready()
    {
        foreach (Stat stat in Enum.GetValues<Stat>())
        {
            _stat.AddItem(NameOf(stat));
        }

        foreach (StatOp op in Enum.GetValues<StatOp>())
        {
            _op.AddItem(NameOf(op));
        }

        _stat.ItemSelected += OnChanged;
        _op.ItemSelected += OnChanged;
        _value.ValueChanged += OnValueChanged;
    }

    public void Bind(MapZoneEffect effect)
    {
        _stat.Select(Array.IndexOf(Enum.GetValues<Stat>(), effect.Stat));
        _op.Select(Array.IndexOf(Enum.GetValues<StatOp>(), effect.Op));
        _value.Value = effect.Value;
    }

    public MapZoneEffect Value => new(
        Enum.GetValues<Stat>()[_stat.Selected],
        Enum.GetValues<StatOp>()[_op.Selected],
        (float)_value.Value);

    public void OnRemovePressed() => RemoveRequested?.Invoke(this);

    private void OnChanged(long _) => Changed?.Invoke();
    private void OnValueChanged(double _) => Changed?.Invoke();
    private static string NameOf<T>(T value) where T : Enum =>
        value.ToString().ToLowerInvariant().Replace('_', ' ');
}
