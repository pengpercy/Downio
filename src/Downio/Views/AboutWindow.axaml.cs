using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Downio.Views;

public partial class AboutWindow : DialogWindow
{
    private const string RepoUrl = "https://github.com/pengpercy/Downio";

    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnRepoLinkClick(object sender, RoutedEventArgs e)
    {
        OpenUrl(RepoUrl);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
                return;
            }

            Process.Start("xdg-open", url);
        }
        catch
        {
        }
    }
}
