using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>Common binding surface implemented by type-specific property
/// control prefabs.</summary>
internal interface IUiPropertyControl
{
    void Bind(IUiPropertyDescriptor descriptor, object model, Action changed);
    void UpdateModel(object model);
    void SetEditable(bool editable);
}
