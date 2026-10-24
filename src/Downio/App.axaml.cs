using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Chrome;
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
    // Acquired in Program.Main before Avalonia starts, so a second launch can exit cleanly.
    internal static SingleInstanceService? SingleInstance { get; set; }
    private bool _isExplicitExitRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Fluent's drawn decorations template sets Spacing="2" on the caption button panel, which beats
        // any style setter. With it the panel came out 2px wider than its buttons on Windows, so the
        // close button ended short of the window edge.
        Control.LoadedEvent.AddClassHandler<StackPanel>((panel, _) =>
        {
            if (panel.Name == "PART_OverlayPanel" && panel.TemplatedParent is WindowDrawnDecorations)
                panel.Spacing = 0;
        });

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
        // Before the window is shown, so the first TaskbarButtonCreated message is not missed.
        taskbarBadgeService.Attach(mainWindow);

        SingleInstance?.SetActivateHandler(viewModel.ToggleMainWindow);

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
            SingleInstance?.Dispose();
        };

        var updateChecked = false;
        mainWindow.Opened += async (_, _) =>
        {
            if (updateChecked) return;
            updateChecked = true;
            await UpdateWindowService.CheckAutomaticallyAsync(viewModel.SettingsService, mainWindow);
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
