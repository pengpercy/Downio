using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Downio.Helpers;
using Downio.Models;
using Downio.Services;

namespace Downio.ViewModels;

// Commands that only the native menus (app menu, menu bar, tray) expose.
public partial class MainWindowViewModel
{
    private static Window? MainWindowOrNull =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;

    [RelayCommand]
    public void HideApp() => MacApplication.Hide();

    [RelayCommand]
    public void HideOtherApps() => MacApplication.HideOthers();

    [RelayCommand]
    public void ShowAllApps() => MacApplication.ShowAll();

    [RelayCommand]
    public void MinimizeWindow()
    {
        if (MainWindowOrNull is { IsVisible: true } window) window.WindowState = WindowState.Minimized;
    }

    [RelayCommand]
    public void ZoomWindow()
    {
        if (MainWindowOrNull is not { IsVisible: true } window) return;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // Goes through Closing, so it hides to the tray or quits according to the user's setting.
    [RelayCommand]
    public void CloseWindow() => MainWindowOrNull?.Close();

    [RelayCommand]
    public async Task OpenDownloadFolder()
    {
        try
        {
            Directory.CreateDirectory(DefaultSavePath);
            var launcher = MainWindowOrNull?.Launcher;
            if (launcher == null || !await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(DefaultSavePath)))
                throw new IOException("The download folder could not be opened.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Open download folder failed: {DefaultSavePath}");
            _notificationService.ShowNotification(GetString("StatusError"), GetString("MessageOpenFolderFailed"), ToastType.Error);
        }
    }
}
