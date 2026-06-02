using Avalonia.Controls;

namespace MacSign.App.Views;

/// <summary>Toggle a style class on a control so drop targets can light up during a drag.</summary>
internal static class DragHighlight
{
    public static void Set(Control control, string @class, bool on)
    {
        if (on)
        {
            if (!control.Classes.Contains(@class)) control.Classes.Add(@class);
        }
        else
        {
            control.Classes.Remove(@class);
        }
    }
}
