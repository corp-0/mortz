namespace Mortz.Core.Ui;

public sealed class UiPropertyDescriptor<TModel, TValue>(
    string name,
    string displayName,
    Func<TModel, TValue> getter,
    Action<TModel, TValue> setter) : IUiPropertyDescriptor<TModel>
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public Type ValueType => typeof(TValue);

    public TValue Get(TModel model) => getter(model);

    public void Set(TModel model, TValue value) => setter(model, value);

    object? IUiPropertyDescriptor<TModel>.GetValue(TModel model) => Get(model);

    void IUiPropertyDescriptor<TModel>.SetValue(TModel model, object? value)
    {
        if (value is TValue typedValue)
        {
            Set(model, typedValue);
            return;
        }
        if (value is null && default(TValue) is null)
        {
            Set(model, default!);
            return;
        }
        throw new ArgumentException(
            $"Property '{Name}' requires {typeof(TValue).Name}, got {value?.GetType().Name ?? "null"}.",
            nameof(value));
    }
}
