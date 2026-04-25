using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using Downio.Services;
using Downio.ViewModels;
using Downio.Views;

namespace Downio;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!SingleInstanceService.TryCreate("Downio", out _singleInstance))
            {
                SingleInstanceService.NotifyExisting("Downio");
                desktop.Shutdown();
                return;
            }

            var viewModel = new MainWindowViewModel();
            DataContext = viewModel;

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            
            desktop.MainWindow = mainWindow;

            _singleInstance?.SetActivateHandler(viewModel.ToggleMainWindow);

            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons is { Count: > 0 })
            {
                var trayIcon = trayIcons[0];
                var iconUri = OperatingSystem.IsMacOS()
                    ? new Uri("avares://Downio/Assets/Branding/Tray/macos.png")
                    : new Uri("avares://Downio/Assets/Branding/Tray/windows.png");
                trayIcon.Icon = new WindowIcon(new Bitmap(AssetLoader.Open(iconUri)));
            }

            mainWindow.Closing += (_, e) =>
            {
                if (!viewModel.IsExitOnClose)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                    return;
                }
                
                _ = viewModel.ShutdownServicesAsync();
            };

            desktop.Exit += (_, _) =>
            {
                _ = viewModel.ShutdownServicesAsync();
                _singleInstance?.Dispose();
            };

            var updateChecked = false;
            mainWindow.Opened += async (_, _) =>
            {
                if (updateChecked) return;
                updateChecked = true;

                var currentVersion = AppVersionProvider.GetCurrentVersion();
                var updateService = new UpdateService();
                ReleaseInfo? release = null;
                try
                {
                    release = await updateService.CheckForUpdatesAsync(currentVersion);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, "Update check failed");
                }
                if (release is null) return;

                var dialog = new UpdateWindow(release);
                await dialog.ShowDialog(mainWindow);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleMainWindow();
        }
    }
}
