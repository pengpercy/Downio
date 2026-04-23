using Avalonia.Interactivity;

namespace Downio.Views;

public partial class InfoDialog : DialogWindow
{
    public InfoDialog()
    {
        InitializeComponent();
    }

    public InfoDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
