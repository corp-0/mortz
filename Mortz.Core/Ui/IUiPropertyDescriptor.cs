namespace Mortz.Core.Ui;

/// <summary>Untyped view: the UI picks a control off ValueType and binds it
/// without reflecting over the model. Min/Max/Step null = keep the control's
/// default.</summary>
public interface IUiPropertyDescriptor
{
    string Name { get; }
    string DisplayName { get; }
    Type ValueType { get; }
    float? Min { get; }
    float? Max { get; }
    float? Step { get; }
    object? GetValue(object model);
    void SetValue(object model, object? value);
}
