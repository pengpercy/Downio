using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Downio.Helpers;

namespace Downio.Views;

public class DialogWindow : Window
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        WindowDragHelper.TryBeginMoveDrag(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                return;
            case Key.Enter:
            {
                var defaultButton = this.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => b.IsDefault);

                if (defaultButton != null)
                {
                    defaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }

                break;
            }
        }
    }
}

