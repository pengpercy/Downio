using System.Threading.Tasks;
using Avalonia.Controls;
using Downio.Views;

namespace Downio.Services;

public static class UpdateWindowService
{
    private static UpdateWindow? _currentWindow;

    public static bool ActivateExisting()
    {
        if (_currentWindow is not { } existing)
        {
            return false;
        }

        if (existing.WindowState == WindowState.Minimized)
        {
            existing.WindowState = WindowState.Normal;
        }

        existing.Activate();
        return true;
    }

    public static async Task ShowAsync(ReleaseInfo release, SettingsService? settingsService, Window owner)
    {
        if (ActivateExisting())
        {
            return;
        }

        var window = new UpdateWindow(release, settingsService);
        _currentWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentWindow, window))
            {
                _currentWindow = null;
            }
        };

        await window.ShowDialog(owner);
    }
}
