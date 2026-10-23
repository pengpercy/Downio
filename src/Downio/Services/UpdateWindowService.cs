using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Downio.Views;

namespace Downio.Services;

/// <summary>
/// Entry points into the update flow. At most one update window exists; asking again brings it forward.
/// </summary>
public static class UpdateWindowService
{
    private static UpdateWindow? _currentWindow;
    private static bool _isPreparingInBackground;

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

    /// <summary>"Check for Updates…": shows the checking stage straight away and carries on from there.</summary>
    public static void CheckInteractively(SettingsService settingsService, Window owner)
    {
        if (ActivateExisting()) return;

        var viewModel = new UpdateViewModel(settingsService);
        Show(viewModel, owner);
        _ = viewModel.CheckAsync();
    }

    /// <summary>Shows the "new version available" prompt for a release found elsewhere (e.g. the About window).</summary>
    public static void ShowAvailable(ReleaseInfo release, SettingsService settingsService, Window owner)
    {
        if (ActivateExisting()) return;

        var viewModel = new UpdateViewModel(settingsService);
        viewModel.ShowAvailable(release);
        Show(viewModel, owner);
    }

    /// <summary>
    /// Launch-time check. Stays silent unless there is a newer, non-skipped release; with automatic
    /// updates on, it downloads and extracts in the background and only surfaces "ready to install".
    /// </summary>
    public static async Task CheckAutomaticallyAsync(SettingsService settingsService, Window owner)
    {
        if (_currentWindow != null || _isPreparingInBackground) return;

        ReleaseInfo? release;
        try
        {
            release = await new UpdateService(settingsService.Settings).CheckForUpdatesAsync(AppVersionProvider.GetCurrentVersion());
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Automatic update check failed");
            return;
        }

        if (release == null || string.Equals(release.TagName, settingsService.Settings.SkipVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var viewModel = new UpdateViewModel(settingsService);
        if (!settingsService.Settings.AutoInstallUpdates)
        {
            if (_currentWindow != null) return;
            viewModel.ShowAvailable(release);
            Show(viewModel, owner);
            return;
        }

        _isPreparingInBackground = true;
        try
        {
            if (!await viewModel.DownloadAndPrepareAsync(release)) return;
        }
        finally
        {
            _isPreparingInBackground = false;
        }

        // The user may have opened the updater manually meanwhile.
        if (_currentWindow != null) return;
        Show(viewModel, owner);
    }

    private static void Show(UpdateViewModel viewModel, Window owner)
    {
        var window = new UpdateWindow(viewModel);
        _currentWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentWindow, window))
            {
                _currentWindow = null;
            }
        };

        // Not modal: Downio stays usable while an update downloads.
        if (owner.IsVisible)
        {
            window.Show(owner);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Show();
        }
    }
}
