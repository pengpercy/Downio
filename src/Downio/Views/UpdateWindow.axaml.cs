using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downio.Services;
using LiveMarkdown.Avalonia;

namespace Downio.Views;

public partial class UpdateWindow : DialogWindow
{
    public UpdateWindow() : this(new UpdateViewModel())
    {
    }

    public UpdateWindow(UpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        // Closing the window (Esc, close button) abandons whatever is in flight.
        Closed += (_, _) => viewModel.Cancel();

        if (Design.IsDesignMode)
        {
            LocalizationService.SwitchLanguage("zh-CN");
        }
    }
}

/// <summary>The stages of an update, matching the Sparkle-style flow the window walks through.</summary>
public enum UpdateStage
{
    Checking,
    Available,
    UpToDate,
    Failed,
    Downloading,
    Extracting,
    ReadyToInstall
}

public partial class UpdateViewModel : ObservableObject
{
    private const string AppName = "Downio";
    private readonly SettingsService? _settingsService;
    private CancellationTokenSource? _cts;
    private string? _stagedUpdatePath;

    public UpdateViewModel() : this(null)
    {
    }

    public UpdateViewModel(SettingsService? settingsService)
    {
        _settingsService = settingsService;
        CurrentVersion = AppVersionProvider.GetCurrentVersion().TrimStart('v');
        AutoInstallUpdates = settingsService?.Settings.AutoInstallUpdates ?? false;
    }

    public event EventHandler? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptMode), nameof(IsProgressMode), nameof(WindowTitle), nameof(StageText),
        nameof(IsIndeterminate), nameof(ShowCancel), nameof(CanCancel), nameof(ShowInstallAndRelaunch), nameof(ShowDismiss))]
    private UpdateStage _stage = UpdateStage.Checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewVersion), nameof(UpdateTitleText), nameof(UpdateDescriptionText))]
    private ReleaseInfo _release = new();

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _detailText = string.Empty;

    [ObservableProperty]
    private bool _autoInstallUpdates;

    public string CurrentVersion { get; }

    public string NewVersion => Release.TagName.TrimStart('v');

    public ObservableStringBuilder ReleaseNotesBuilder { get; } = new();

    public bool IsPromptMode => Stage == UpdateStage.Available;
    public bool IsProgressMode => !IsPromptMode;
    public bool IsIndeterminate => Stage == UpdateStage.Checking;
    public bool ShowCancel => Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Extracting;
    public bool CanCancel => Stage != UpdateStage.Extracting;
    public bool ShowInstallAndRelaunch => Stage == UpdateStage.ReadyToInstall;
    public bool ShowDismiss => Stage is UpdateStage.UpToDate or UpdateStage.Failed;

    public string WindowTitle => IsPromptMode
        ? GetString("UpdateWindowTitlePrompt", "Software Update")
        : Format("UpdateWindowTitleProgress", "Updating {0}", AppName);

    public string UpdateTitleText => Format("TitleNewVersionAvailable", "A new version of {0} is available!", AppName);

    public string UpdateDescriptionText => Format("UpdateDescription",
        "{0} {1} is now available—you have {2}. Would you like to download it now?", AppName, NewVersion, CurrentVersion);

    public string StageText => Stage switch
    {
        UpdateStage.Checking => GetString("UpdateStageChecking", "Checking for updates…"),
        UpdateStage.Downloading => GetString("UpdateStageDownloading", "Downloading update…"),
        UpdateStage.Extracting => GetString("UpdateStageExtracting", "Extracting update…"),
        UpdateStage.ReadyToInstall => GetString("UpdateStageReady", "Ready to Install"),
        UpdateStage.UpToDate => GetString("UpdateStageUpToDate", "You're up to date!"),
        UpdateStage.Failed => GetString("UpdateStageFailed", "Update Failed"),
        _ => string.Empty
    };

    partial void OnReleaseChanged(ReleaseInfo value)
    {
        ReleaseNotesBuilder.Clear();
        ReleaseNotesBuilder.Append(value.Body ?? string.Empty);
    }

    partial void OnAutoInstallUpdatesChanged(bool value)
    {
        if (_settingsService == null) return;
        _settingsService.Settings.AutoInstallUpdates = value;
        _settingsService.Save();
    }

    /// <summary>Manual check: the window is already showing the "Checking" stage.</summary>
    public async Task CheckAsync()
    {
        var cts = ResetCancellation();
        Stage = UpdateStage.Checking;
        try
        {
            var release = await new UpdateService(_settingsService?.Settings)
                .CheckForUpdatesAsync(CurrentVersion).WaitAsync(cts.Token);
            if (release == null)
            {
                DetailText = Format("UpdateUpToDateDetail", "{0} {1} is currently the newest version available.", AppName, CurrentVersion);
                Stage = UpdateStage.UpToDate;
                return;
            }

            ShowAvailable(release);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Update check failed");
            Fail(ex);
        }
    }

    public void ShowAvailable(ReleaseInfo release)
    {
        Release = release;
        Stage = UpdateStage.Available;
    }

    /// <summary>Downloads and extracts the release; on success the stage is <see cref="UpdateStage.ReadyToInstall"/>.</summary>
    public async Task<bool> DownloadAndPrepareAsync(ReleaseInfo release)
    {
        Release = release;
        var cts = ResetCancellation();
        try
        {
            var asset = UpdateAssetSelector.SelectPreferredAsset(release)
                        ?? throw new InvalidOperationException(GetString("UpdateStatusNoPackage", "No compatible update package found."));

            Stage = UpdateStage.Downloading;
            Progress = 0;
            DetailText = string.Empty;
            var updateDir = Path.Combine(Path.GetTempPath(), "Downio", "updates", NewVersion);
            Directory.CreateDirectory(updateDir);
            var downloadPath = Path.Combine(updateDir, asset.Name);

            var downloadProgress = new Progress<(long Received, long Total)>(p =>
            {
                Progress = p.Total > 0 ? (double)p.Received / p.Total : 0;
                DetailText = p.Total > 0
                    ? $"{FormatSize(p.Received)} / {FormatSize(p.Total)}"
                    : FormatSize(p.Received);
            });
            await new UpdateService(_settingsService?.Settings).DownloadUpdateAsync(asset, downloadPath, downloadProgress, cts.Token);

            Stage = UpdateStage.Extracting;
            Progress = 0;
            DetailText = string.Empty;
            _stagedUpdatePath = await UpdateApplier.PrepareAsync(downloadPath, new Progress<double>(p => Progress = p), cts.Token);

            Progress = 1;
            Stage = UpdateStage.ReadyToInstall;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Preparing update {release.TagName} failed");
            Fail(ex);
            return false;
        }
    }

    [RelayCommand]
    private Task InstallUpdate() => DownloadAndPrepareAsync(Release);

    [RelayCommand]
    private void SkipThisVersion()
    {
        if (_settingsService != null)
        {
            _settingsService.Settings.SkipVersion = Release.TagName;
            _settingsService.Save();
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemindMeLater() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Dismiss() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CancelUpdate()
    {
        Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void InstallAndRelaunch()
    {
        if (_stagedUpdatePath == null || !UpdateApplier.TryInstallAndRelaunch(_stagedUpdatePath))
        {
            DetailText = GetString("UpdateStatusFailed", "Update failed.");
            Stage = UpdateStage.Failed;
            return;
        }

        // Quit through the normal path so aria2 is stopped before the installer replaces files.
        if (Application.Current is App app)
        {
            app.RequestExplicitExit();
        }
        else if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Cancel() => _cts?.Cancel();

    private CancellationTokenSource ResetCancellation()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return _cts;
    }

    private void Fail(Exception ex)
    {
        DetailText = ex is InvalidOperationException ? ex.Message : $"{GetString("UpdateStatusFailed", "Update failed.")} {ex.Message}";
        Stage = UpdateStage.Failed;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB"
    };

    private static string Format(string key, string fallback, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, GetString(key, fallback), args);

    private static string GetString(string key, string fallback)
    {
        if (Application.Current != null && Application.Current.TryGetResource(key, null, out var resource) && resource is string str)
        {
            return str;
        }
        return fallback;
    }
}
