using Avalonia.Input;
using Avalonia.Interactivity;

namespace Downio.Views;

public partial class ConfirmDeleteDialog : DialogWindow
{
    public bool DeleteFile => DeleteFileCheckBox.IsChecked ?? false;

    public ConfirmDeleteDialog()
    {
        InitializeComponent();
    }

    public ConfirmDeleteDialog(string fileName) : this()
    {
        // Use default if resource not found
        var format = GetString("MessageConfirmDeleteTask");
        // Fallback if resource is missing or empty
        if (string.IsNullOrEmpty(format) || format == "MessageConfirmDeleteTask")
        {
             // Fallback to Chinese if system seems to be Chinese (simple check)
             var culture = System.Globalization.CultureInfo.CurrentCulture.Name;
             if (culture.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
             {
                 format = "确定要移除“%s”下载任务吗？";
             }
             else
             {
                 format = "Are you sure you want to remove task \"%s\"?";
             }
        }
        
        // Ensure fileName is not null
        fileName ??= "Unknown";
        
        try
        {
            MessageText.Text = format.Replace("%s", fileName);
        }
        catch (System.Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Failed to format delete message: {ex.Message}");
             MessageText.Text = $"Delete {fileName}?";
        }
    }

    public ConfirmDeleteDialog(string title, string message, bool showDeleteFile = false) : this()
    {
        Title = title;
        MessageText.Text = message;
        DeleteFileCheckBox.IsVisible = showDeleteFile;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private string GetString(string key)
    {
        if (Avalonia.Application.Current != null &&
            Avalonia.Application.Current.TryGetResource(key, null, out var resource) &&
            resource is string str)
        {
            return str;
        }
        return key; // Fallback
    }
}
