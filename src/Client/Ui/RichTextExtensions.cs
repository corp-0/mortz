using Godot;
using Mortz.Core.Text;

namespace Mortz.Client.Ui;

/// <summary>Godot-side sugar for the RichText builder.</summary>
public static class RichTextExtensions
{
    public static Style Color(this Style style, Color color) =>
        style.Color("#" + color.ToHtml(false));
}
