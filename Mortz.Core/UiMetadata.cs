namespace Mortz.Core;

/// <summary>Opens a category at this property; the [UiProperty] members below
/// stay in it until the next marker.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiCategoryAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UiPropertyAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
}

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

public sealed class UiCategoryDescriptor<TModel>(
    string displayName,
    IReadOnlyList<IUiPropertyDescriptor<TModel>> properties)
{
    public string DisplayName { get; } = displayName;
    public IReadOnlyList<IUiPropertyDescriptor<TModel>> Properties { get; } = properties;
}
