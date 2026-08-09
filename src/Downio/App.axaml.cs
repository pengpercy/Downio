using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using Downio.Services;
using Downio.Services.TaskbarBadge;
using Downio.ViewModels;
using Downio.Views;

namespace Downio;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private bool _isExplicitExitRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var settings = new SettingsService();
        LocalizationService.Initialize(settings.Settings.Language);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            NotificationService.InitializePlatformIntegration();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Platform notification integration initialization failed");
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!SingleInstanceService.TryCreate("Downio", out _singleInstance))
            {
                if (SingleInstanceService.NotifyExisting("Downio"))
                {
                    desktop.Shutdown();
                    return;
                }

                AppLog.Warn("Existing Downio instance was detected but could not be activated; continuing startup.");
                _singleInstance = null;
            }
            else
            {
                AppLog.Info("Single instance lock acquired.");
            }

            try
            {
                InitializeDesktopApplication(desktop);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "Desktop application initialization failed");
                return;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeDesktopApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var taskbarBadgeService = new TaskbarBadgeService();
        var viewModel = new MainWindowViewModel(taskbarBadgeService);
        DataContext = viewModel;

        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };
        
        desktop.MainWindow = mainWindow;
        mainWindow.Opened += (_, _) => taskbarBadgeService.Attach(mainWindow);

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
            if (!_isExplicitExitRequested && !viewModel.IsExitOnClose)
            {
                e.Cancel = true;
                mainWindow.Hide();
                return;
            }
            
            _ = viewModel.ShutdownServicesAsync();
        };

        desktop.ShutdownRequested += (_, _) =>
        {
            _isExplicitExitRequested = true;
            viewModel.RequestQuit();
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
            var updateService = new UpdateService(viewModel.SettingsService.Settings);
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

            await UpdateWindowService.ShowAsync(release, viewModel.SettingsService, mainWindow);
        };
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleMainWindow();
        }
    }

    public void RequestExplicitExit()
    {
        _isExplicitExitRequested = true;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.RequestQuit();
        }

        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (desktop.MainWindow is { } mainWindow)
        {
            mainWindow.Close();
            return;
        }

        desktop.Shutdown();
    }
}
