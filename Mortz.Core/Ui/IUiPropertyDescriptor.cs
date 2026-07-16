namespace Mortz.Core.Ui;

/// <summary>Untyped view: the UI picks a control off ValueType and binds it
/// without reflecting over the model.</summary>
public interface IUiPropertyDescriptor<TModel>
{
    string Name { get; }
    string DisplayName { get; }
    Type ValueType { get; }
    object? GetValue(TModel model);
    void SetValue(TModel model, object? value);
}
