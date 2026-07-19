using Mortz.Core.Ui;

namespace Mortz.Client.Ui;

/// <summary>What UiPropertySheet calls on a type-specific control prefab.</summary>
internal interface IUiPropertyControl
{
    void Bind(IUiPropertyDescriptor descriptor, object model, Action changed);
    void UpdateModel(object model);
    void SetEditable(bool editable);
}
