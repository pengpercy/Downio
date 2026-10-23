using Avalonia.Controls;

namespace Downio.Views;

public partial class Ed2kToolbar : UserControl
{
    public Ed2kToolbar()
    {
        InitializeComponent();
    }

    public void FocusSearchBox() => SearchBox.Focus();
}
