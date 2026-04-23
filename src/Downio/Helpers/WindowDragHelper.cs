using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Downio.Helpers;

public static class WindowDragHelper
{
    public static void TryBeginMoveDrag(Window window, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(window);
        if (!point.Properties.IsLeftButtonPressed) return;

        var control = e.Source as Control;
        while (control != null)
        {
            if (control is Button 
                or TextBox 
                or CheckBox 
                or ToggleSwitch 
                or ComboBox 
                or Slider 
                or ScrollBar 
                or ScrollViewer 
                or MenuItem 
                or ListBox 
                or ListBoxItem)
            {
                return;
            }

            control = control.GetVisualParent() as Control;
        }

        window.BeginMoveDrag(e);
    }
}
