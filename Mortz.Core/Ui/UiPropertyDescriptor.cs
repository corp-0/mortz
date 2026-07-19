namespace Mortz.Core.Ui;

public sealed class UiPropertyDescriptor<TModel, TValue>(
    string name,
    string displayName,
    Func<TModel, TValue> getter,
    Action<TModel, TValue> setter,
    float? min = null,
    float? max = null,
    float? step = null) : IUiPropertyDescriptor
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public Type ValueType => typeof(TValue);
    public float? Min { get; } = min;
    public float? Max { get; } = max;
    public float? Step { get; } = step;

    public TValue Get(TModel model) => getter(model);

    public void Set(TModel model, TValue value) => setter(model, value);

    object? IUiPropertyDescriptor.GetValue(object model) => Get(TypedModel(model));

    void IUiPropertyDescriptor.SetValue(object model, object? value)
    {
        TModel typedModel = TypedModel(model);
        if (value is TValue typedValue)
        {
            Set(typedModel, typedValue);
            return;
        }
        if (value is null && default(TValue) is null)
        {
            Set(typedModel, default!);
            return;
        }
        throw new ArgumentException(
            $"Property '{Name}' requires {typeof(TValue).Name}, got {value?.GetType().Name ?? "null"}.",
            nameof(value));
    }

    private TModel TypedModel(object model)
    {
        if (model is TModel typed)
            return typed;
        throw new ArgumentException(
            $"Property '{Name}' binds {typeof(TModel).Name}, got {model.GetType().Name}.",
            nameof(model));
    }
}
