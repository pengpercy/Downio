using Avalonia.Controls;
using Avalonia.Interactivity;
using Downio.Services;
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using System.Globalization;
using LiveMarkdown.Avalonia;

namespace Downio.Views;

public partial class UpdateWindow : DialogWindow
{
    private readonly UpdateViewModel _viewModel;

    public UpdateWindow()
    {
        InitializeComponent();
        _viewModel = new UpdateViewModel();
        DataContext = _viewModel;

        if (Design.IsDesignMode)
        {
            LocalizationService.SwitchLanguage("zh-CN");
        }
    }

    public UpdateWindow(ReleaseInfo release, SettingsService? settingsService = null) : this()
    {
        _viewModel.Release = release;
        _viewModel.CurrentVersion = AppVersionProvider.GetCurrentVersion();
        _viewModel.SettingsService = settingsService;
        
        if (settingsService != null)
        {
            _viewModel.AutoInstallUpdates = settingsService.Settings.AutoInstallUpdates;
        }
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettings();
        Close();
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveSettings();
        // Change window size when starting download
        this.Width = 480;
        this.Height = 240;
        this.MinWidth = 480;
        this.MinHeight = 240;
        
        // Center window again after resize
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        await _viewModel.StartDownloadAsync(this);
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.ApplyUpdateAndRelaunchAsync();
    }
}

public partial class UpdateViewModel : ObservableObject
{
    public SettingsService? SettingsService { get; set; }

    [ObservableProperty]
    private ReleaseInfo _release = new();

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private bool _isReadyToInstall = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _currentVersion = string.Empty;

    [ObservableProperty]
    private bool _autoInstallUpdates;

    public ObservableStringBuilder ReleaseNotesBuilder { get; } = new();

    public string AppName => "Downio";

    public string WindowTitle => GetString("TitleUpdateAvailable", "Update Available");

    public string VersionText => Release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Release.TagName : $"v{Release.TagName}";
    public string ReleaseNotes => Release.Body;

    public string UpdateTitleText => string.Format(CultureInfo.CurrentCulture, GetString("TitleNewVersionAvailable", "A new version of {0} is available!"), AppName);

    public string UpdateDescriptionText => string.Format(CultureInfo.CurrentCulture, GetString("UpdateDescription", "{0} {1} is now available - you have {2}. Would you like to download it now?"), AppName, VersionText, CurrentVersion.StartsWith("v") ? CurrentVersion : $"v{CurrentVersion}");

    public string UpdateFromToText
    {
        get
        {
            var from = CurrentVersion.StartsWith("v") ? CurrentVersion : $"v{CurrentVersion}";
            var to = VersionText;
            return string.Format(CultureInfo.CurrentCulture, GetString("UpdateFromTo", "Update from {0} to {1}"), from, to);
        }
    }

    public string ButtonSkipText => GetString("ButtonSkipVersion", "Skip This Version");
    public string ButtonLaterText => GetString("ButtonRemindLater", "Remind Me Later");
    public string ButtonInstallText => GetString("ButtonInstallUpdate", "Install Update");
    public string ButtonInstallRelaunchText => GetString("ButtonInstallAndRelaunch", "Install and Relaunch");
    public string AutoUpdateLabelText => GetString("LabelAutoUpdate", "Automatically download and install updates in the future");

    partial void OnReleaseChanged(ReleaseInfo value)
    {
        ReleaseNotesBuilder.Clear();
        ReleaseNotesBuilder.Append(Release.Body ?? string.Empty);

        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(UpdateFromToText));
        OnPropertyChanged(nameof(UpdateTitleText));
        OnPropertyChanged(nameof(UpdateDescriptionText));
        OnPropertyChanged(nameof(ButtonSkipText));
        OnPropertyChanged(nameof(ButtonLaterText));
        OnPropertyChanged(nameof(ButtonInstallText));
        OnPropertyChanged(nameof(AutoUpdateLabelText));
    }

    partial void OnCurrentVersionChanged(string value)
    {
        OnPropertyChanged(nameof(UpdateFromToText));
        OnPropertyChanged(nameof(UpdateDescriptionText));
    }

    private string? _downloadedFilePath;

    [RelayCommand]
    public void SkipThisVersion()
    {
        if (SettingsService != null)
        {
            SettingsService.Settings.SkipVersion = Release.TagName;
            SettingsService.Settings.AutoInstallUpdates = AutoInstallUpdates;
            SettingsService.Save();
        }
    }

    [RelayCommand]
    public void RemindMeLater()
    {
        if (SettingsService != null)
        {
            SettingsService.Settings.AutoInstallUpdates = AutoInstallUpdates;
            SettingsService.Save();
        }
    }

    public void SaveSettings()
    {
        if (SettingsService != null)
        {
            SettingsService.Settings.AutoInstallUpdates = AutoInstallUpdates;
            SettingsService.Save();
        }
    }

    public async Task StartDownloadAsync(Window window)
    {
        if (IsDownloading) return;

        IsDownloading = true;
        StatusText = GetString("UpdateStatusPreparing", "Preparing...");
        DownloadProgress = 0;

        var updateService = new UpdateService(SettingsService?.Settings);
        var asset = UpdateAssetSelector.SelectPreferredAsset(Release);
        if (asset is null)
        {
            IsDownloading = false;
            StatusText = GetString("UpdateStatusNoPackage", "No compatible update package found.");
            return;
        }

        var version = Release.TagName.TrimStart('v');
        var updateDir = Path.Combine(Path.GetTempPath(), "Downio", "updates", version);
        Directory.CreateDirectory(updateDir);
        _downloadedFilePath = Path.Combine(updateDir, asset.Name);

        var progress = new Progress<double>(p =>
        {
            DownloadProgress = p;
            var percent = (int)(p * 100);
            StatusText = string.Format(CultureInfo.CurrentCulture, GetString("UpdateStatusDownloading", "Downloading... {0}%"), percent);
        });

        try
        {
            await updateService.DownloadUpdateAsync(asset.BrowserDownloadUrl, _downloadedFilePath, progress);
            IsReadyToInstall = true;
            StatusText = GetString("LabelUpdateReady", "Update downloaded successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download failed: {ex.Message}");
            IsDownloading = false;
            StatusText = GetString("UpdateStatusFailed", "Update failed.");
        }
    }

    public async Task ApplyUpdateAndRelaunchAsync()
    {
        if (string.IsNullOrEmpty(_downloadedFilePath)) return;

        try
        {
            StatusText = GetString("UpdateStatusApplying", "Applying update...");
            var version = Release.TagName.TrimStart('v');
            var applied = await UpdateApplier.TryApplyUpdateAsync(_downloadedFilePath, version);
            if (!applied)
            {
                OpenDownloadedFile(_downloadedFilePath);
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Apply update failed: {ex.Message}");
            StatusText = GetString("UpdateStatusFailed", "Update failed.");
        }
    }

    private static void OpenDownloadedFile(string path)
    {
        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                Process.Start("xdg-open", path);
            }
        }
        catch
        {
        }
    }

    private static string GetString(string key, string fallback)
    {
        if (Application.Current != null && Application.Current.TryGetResource(key, null, out var resource) && resource is string str)
        {
            return str;
        }
        return fallback;
    }
}
