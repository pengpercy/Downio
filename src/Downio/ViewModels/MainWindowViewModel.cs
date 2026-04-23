using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downio.Models;
using Downio.Services;
using Downio.Services.Aria2;
using Downio.Views;
using Downio.Helpers;

namespace Downio.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAria2Service _aria2Service;
    private readonly SettingsService _settingsService;
    private readonly AutoStartService _autoStartService;
    private readonly NotificationService _notificationService;
    private readonly TaskListView _taskListView;
    private readonly SettingsView _settingsView;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<string, string> _lastStatusByGid = new();
    private bool _isShuttingDown;
    private readonly bool _windowControlsOnLeft;

    [ObservableProperty]
    private object _currentView = null!;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private string _currentTitleKey = "MenuDownloading";

    partial void OnCurrentTitleKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsSettings));
        
        // Refresh list when switching views if needed
        if (value == "MenuDownloading" || value == "MenuWaiting" || value == "MenuStopped")
        {
            _ = RefreshTaskListAsync();
        }
    }

    public bool IsDownloading => CurrentTitleKey == "MenuDownloading";
    public bool IsWaiting => CurrentTitleKey == "MenuWaiting";
    public bool IsStopped => CurrentTitleKey == "MenuStopped";
    public bool IsSettings => CurrentTitleKey == "MenuSettings";

    public bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public bool IsMacLikeLayout => IsMacOS || (IsLinux && _windowControlsOnLeft);
    public bool IsWindowsLikeLayout => !IsMacLikeLayout;
    public bool IsNotMacOS => !IsMacLikeLayout;

    [ObservableProperty]
    private SettingsSection _selectedSettingsSection = SettingsSection.General;

    public string CurrentSettingsTitleKey => SelectedSettingsSection switch
    {
        SettingsSection.General => "SettingsGeneral",
        SettingsSection.Appearance => "SettingsAppearance",
        SettingsSection.Network => "SettingsNetwork",
        SettingsSection.Advanced => "SettingsAdvanced",
        SettingsSection.About => "SettingsAbout",
        _ => "MenuSettings"
    };

    public bool IsSettingsGeneral => SelectedSettingsSection == SettingsSection.General;
    public bool IsSettingsAppearance => SelectedSettingsSection == SettingsSection.Appearance;
    public bool IsSettingsNetwork => SelectedSettingsSection == SettingsSection.Network;
    public bool IsSettingsAdvanced => SelectedSettingsSection == SettingsSection.Advanced;
    public bool IsSettingsAbout => SelectedSettingsSection == SettingsSection.About;

    partial void OnSelectedSettingsSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(CurrentSettingsTitleKey));
        OnPropertyChanged(nameof(IsSettingsGeneral));
        OnPropertyChanged(nameof(IsSettingsAppearance));
        OnPropertyChanged(nameof(IsSettingsNetwork));
        OnPropertyChanged(nameof(IsSettingsAdvanced));
        OnPropertyChanged(nameof(IsSettingsAbout));
    }

    public Thickness SidebarToggleMargin
    {
        get
        {
            return new Thickness(16, 0, 0, 0);
        }
    }

    [ObservableProperty]
    private Thickness _macToggleMargin = new(76, 6, 0, 0);

    [ObservableProperty]
    private Thickness _titleBarToolsMargin = new(0);

    partial void OnIsPaneOpenChanged(bool value)
    {
        UpdateTitleBarToolsMargin();
    }

    public void UpdateMacTitleBarInsets(double trafficLightsRight, double titleBarHeight)
    {
        var spacing = 8d;
        var toggleSize = 32d;
        var top = Math.Max(0, (titleBarHeight - toggleSize) / 2);
        var left = Math.Max(0, trafficLightsRight + spacing);

        MacToggleMargin = new Thickness(left, top, 0, 0);
        UpdateTitleBarToolsMargin();
    }

    private void UpdateTitleBarToolsMargin()
    {
        if (!IsMacLikeLayout)
        {
            TitleBarToolsMargin = new Thickness(0);
            return;
        }

        if (IsPaneOpen)
        {
            TitleBarToolsMargin = new Thickness(0);
        }
        else
        {
            var compactWidth = 64d; 
            var baseMargin = 8d;    
            var toggleWidth = 32d;
            var spacing = 8d;
            
            var toggleRightEdge = MacToggleMargin.Left + toggleWidth + spacing;
            var toolbarStartWithoutExtraMargin = compactWidth + baseMargin;
            
            var desiredExtraMargin = Math.Max(0, toggleRightEdge - toolbarStartWithoutExtraMargin);
            TitleBarToolsMargin = new Thickness(desiredExtraMargin, 0, 0, 0);
        }
    }

    [ObservableProperty]
    private ObservableCollection<DownloadTask> _tasks = new();

    [ObservableProperty]
    private DownloadTask? _selectedTask;

    public List<DownloadTask> SelectedTasks { get; private set; } = new();

    public void UpdateSelectedTasks(List<DownloadTask> tasks)
    {
        SelectedTasks = tasks;
        DeleteSelectedTasksCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedTasks))]
    public async Task DeleteSelectedTasks()
    {
        if (SelectedTasks.Count > 0)
        {
            var tasksToDelete = SelectedTasks.ToList(); // Clone list to avoid modification issues during enumeration

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
            {
                var dialog = new ConfirmDeleteDialog(tasksToDelete.Count == 1 ? tasksToDelete[0].Name : $"{tasksToDelete.Count} tasks");
                var result = await dialog.ShowDialog<bool>(mainWindow);
                
                if (result)
                {
                    foreach (var task in tasksToDelete)
                    {
                        await _aria2Service.RemoveAsync(task.Id);
                        
                        if (dialog.DeleteFile && !string.IsNullOrEmpty(task.FilePath))
                        {
                            try
                            {
                                if (File.Exists(task.FilePath)) File.Delete(task.FilePath);
                                var aria2File = task.FilePath + ".aria2";
                                if (File.Exists(aria2File)) File.Delete(aria2File);
                            }
                            catch { /* Ignore delete errors */ }
                        }
                    }
                    
                    SelectedTask = null;
                    await RefreshTaskListAsync();
                    
                    if (tasksToDelete.Count == 1)
                    {
                        var msg = tasksToDelete[0].Name + (dialog.DeleteFile ? GetString("NotificationAlsoDeletedFile") : string.Empty);
                        _notificationService.ShowNotification(GetString("NotificationTaskDeleted"), msg, ToastType.Success);
                    }
                    else
                    {
                        var msg = string.Format(GetString("NotificationTasksDeleted"), tasksToDelete.Count) + (dialog.DeleteFile ? GetString("NotificationAlsoDeletedFile") : string.Empty);
                        _notificationService.ShowNotification(GetString("NotificationTaskDeleted"), msg, ToastType.Success);
                    }
                }
            }
        }
    }

    private bool CanDeleteSelectedTasks() => SelectedTasks.Count > 0;

    [RelayCommand]
    public async Task RefreshTasks()
    {
        await RefreshTaskListAsync();
    }

    [RelayCommand]
    public async Task ToggleTaskState(DownloadTask? task)
    {
        if (task == null) return;

        if (task.Status == "StatusDownloading" || task.Status == "StatusWaiting")
        {
            await _aria2Service.PauseAsync(task.Id);
        }
        else if (task.Status == "StatusPaused")
        {
            await _aria2Service.UnpauseAsync(task.Id);
        }

        await RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowTaskDetails(DownloadTask? task)
    {
        if (task == null) return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
        var dialog = new TaskDetailsWindow(task);
        dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    public Task OpenFolder(DownloadTask? task)
    {
        if (task == null || string.IsNullOrEmpty(task.FilePath)) return Task.CompletedTask;

        var path = task.FilePath;
        var dir = Path.GetDirectoryName(path);

        if (!Directory.Exists(dir)) return Task.CompletedTask;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"-R \"{path}\"");
            }
            else
            {
                Process.Start("xdg-open", dir);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open folder failed: {ex.Message}");
            AppLog.Error(ex, $"Open folder failed: {task.Name} ({task.Id})");
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task CopyLink(DownloadTask? task)
    {
        if (task == null || string.IsNullOrEmpty(task.Url)) return;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
            {
                var clipboard = mainWindow.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(task.Url);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy link failed: {ex.Message}");
            AppLog.Error(ex, $"Copy link failed: {task.Name} ({task.Id})");
        }
    }

    [RelayCommand]
    public void ShowAbout()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
        var dialog = new AboutWindow
        {
            DataContext = this
        };
        dialog.ShowDialog(mainWindow);
    }

    [RelayCommand]
    public void ToggleMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
            
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            
            window.Activate();
        }
    }

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isAddTaskVisible;

    [ObservableProperty]
    private int _newTaskInputModeIndex;

    [ObservableProperty]
    private string _newTaskUrl = string.Empty;

    [ObservableProperty]
    private string _newTaskTorrentFilePath = string.Empty;

    public string NewTaskTorrentFileName =>
        string.IsNullOrWhiteSpace(NewTaskTorrentFilePath) ? string.Empty : Path.GetFileName(NewTaskTorrentFilePath);

    partial void OnNewTaskTorrentFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(NewTaskTorrentFileName));
    }

    [ObservableProperty]
    private string _newTaskName = string.Empty;

    [ObservableProperty]
    private int _newTaskChunks = 4;

    [ObservableProperty]
    private string _newTaskSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    [ObservableProperty]
    private bool _newTaskShowAdvanced;

    [ObservableProperty]
    private string _newTaskUserAgent = string.Empty;

    [ObservableProperty]
    private string _newTaskAuthorization = string.Empty;

    [ObservableProperty]
    private string _newTaskReferer = string.Empty;

    [ObservableProperty]
    private string _newTaskCookie = string.Empty;

    [ObservableProperty]
    private string _newTaskProxy = string.Empty;

    // Advanced Settings Properties
    private readonly System.Threading.SemaphoreSlim _aria2RecoveryLock = new(1, 1);
    private DateTimeOffset _lastAria2RecoveryAttempt = DateTimeOffset.MinValue;
    public ObservableCollection<TrackerSourceOption> TrackerSourceOptions { get; } = new();

    public ObservableCollection<TrackerSourceOption> SelectedTrackerSourceOptions { get; } = new();

    [ObservableProperty]
    private bool _isSyncingTrackers;

    [ObservableProperty]
    private string _newTrackerSourceUrl = string.Empty;

    [RelayCommand]
    private void AddTrackerSource()
    {
        var url = (NewTrackerSourceUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            _notificationService.ShowNotification("Tracker", "Invalid tracker source URL.", ToastType.Warning);
            return;
        }

        if (TrackerSourceOptions.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            var exist = TrackerSourceOptions.First(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            exist.IsSelected = true;
            NewTrackerSourceUrl = string.Empty;
            RefreshSelectedTrackerSources();
            return;
        }

        var isCdn = url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase);
        var option = new TrackerSourceOption("Custom", url, isCdn, true)
        {
            IsSelected = true
        };
        option.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(TrackerSourceOption.IsSelected), StringComparison.Ordinal))
            {
                RefreshSelectedTrackerSources();
            }
        };

        TrackerSourceOptions.Add(option);

        _settingsService.Settings.CustomTrackerSources ??= new List<string>();
        if (!_settingsService.Settings.CustomTrackerSources.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            _settingsService.Settings.CustomTrackerSources.Add(url);
        }
        _settingsService.Save();

        NewTrackerSourceUrl = string.Empty;
        RefreshSelectedTrackerSources();
    }

    [RelayCommand]
    public void RemoveTrackerSource(TrackerSourceOption? option)
    {
        if (option == null) return;
        if (!option.IsCustom) return;

        TrackerSourceOptions.Remove(option);
        SelectedTrackerSourceOptions.Remove(option);

        _settingsService.Settings.CustomTrackerSources ??= new List<string>();
        _settingsService.Settings.CustomTrackerSources.RemoveAll(x => string.Equals(x, option.Url, StringComparison.OrdinalIgnoreCase));
        _settingsService.Save();

        RefreshSelectedTrackerSources();
    }

    [ObservableProperty]
    private bool _autoSyncTracker;

    partial void OnAutoSyncTrackerChanged(bool value)
    {
        _settingsService.Settings.AutoSyncTracker = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private long _lastSyncTrackerTime;

    public string LastSyncTrackerTimeText
    {
        get
        {
            if (LastSyncTrackerTime <= 0) return "-";
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(LastSyncTrackerTime).LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture);
            }
            catch
            {
                return "-";
            }
        }
    }

    partial void OnLastSyncTrackerTimeChanged(long value)
    {
        _settingsService.Settings.LastSyncTrackerTime = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(LastSyncTrackerTimeText));
    }

    [RelayCommand]
    private async Task SyncTrackersAsync()
    {
        if (IsSyncingTrackers) return;

        var sources = SelectedTrackerSourceOptions.Select(x => x.Url).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count == 0)
        {
            sources = _settingsService.Settings.TrackerSources.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        if (sources.Count == 0)
        {
            _notificationService.ShowNotification("Tracker", "No tracker source selected.", ToastType.Warning);
            return;
        }

        IsSyncingTrackers = true;
        try
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(ProxyAddress) && ProxyPort > 0 && ProxyTypeIndex == 0)
            {
                handler.Proxy = new WebProxy(ProxyAddress, ProxyPort);
                handler.UseProxy = true;
            }

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Downio/1.0");

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var tasks = sources.Select(async url =>
            {
                var requestUrl = url.Contains('?') ? $"{url}&t={now}" : $"{url}?t={now}";
                return await client.GetStringAsync(requestUrl).ConfigureAwait(false);
            }).ToList();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var trackers = results
                .SelectMany(text => (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            BtTrackers = string.Join('\n', trackers);
            await _aria2Service.ApplyBtTrackersAsync(BtTrackers).ConfigureAwait(false);
            LastSyncTrackerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _notificationService.ShowNotification("Tracker", "Tracker list synced.", ToastType.Success);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to sync trackers");
            _notificationService.ShowNotification("Tracker", "Failed to sync tracker list.", ToastType.Error);
        }
        finally
        {
            IsSyncingTrackers = false;
        }
    }

    [ObservableProperty]
    private string _btTrackers = string.Empty;

    partial void OnBtTrackersChanged(string value)
    {
        _settingsService.Settings.BtTrackers = value;
        _settingsService.Save();
        // For BT trackers, we might need a restart or a global option change if supported
    }

    [ObservableProperty]
    private int _rpcPort = 16800;

    partial void OnRpcPortChanged(int value)
    {
        _settingsService.Settings.RpcPort = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private string _rpcSecret = "DownioSecret";

    partial void OnRpcSecretChanged(string value)
    {
        _settingsService.Settings.RpcSecret = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _enableUpnp;

    partial void OnEnableUpnpChanged(bool value)
    {
        _settingsService.Settings.EnableUpnp = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private int _btListenPort = 6881;

    partial void OnBtListenPortChanged(int value)
    {
        _settingsService.Settings.BtListenPort = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private int _dhtListenPort = 6881;

    partial void OnDhtListenPortChanged(int value)
    {
        _settingsService.Settings.DhtListenPort = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private string _globalUserAgent = string.Empty;

    partial void OnGlobalUserAgentChanged(string value)
    {
        _settingsService.Settings.GlobalUserAgent = value;
        _settingsService.Save();
    }

    private void RefreshSelectedTrackerSources()
    {
        var selected = TrackerSourceOptions.Where(x => x.IsSelected).ToList();

        SelectedTrackerSourceOptions.Clear();
        foreach (var item in selected)
        {
            SelectedTrackerSourceOptions.Add(item);
        }

        _settingsService.Settings.TrackerSources = selected.Select(x => x.Url).ToList();
        _settingsService.Save();
    }

    private void InitializeTrackerSourceOptions()
    {
        TrackerSourceOptions.Clear();

        var sources = new List<TrackerSourceOption>
        {
            new("ngosang/trackerslist", "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best.txt", false, false),
            new("ngosang/trackerslist", "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_best_ip.txt", false, false),
            new("ngosang/trackerslist", "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_all.txt", false, false),
            new("ngosang/trackerslist", "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_all_ip.txt", false, false),
            new("ngosang/trackerslist", "https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_best.txt", true, false),
            new("ngosang/trackerslist", "https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_best_ip.txt", true, false),
            new("ngosang/trackerslist", "https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_all.txt", true, false),
            new("ngosang/trackerslist", "https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_all_ip.txt", true, false),
            new("XIU2/TrackersListCollection", "https://raw.githubusercontent.com/XIU2/TrackersListCollection/master/best.txt", false, false),
            new("XIU2/TrackersListCollection", "https://raw.githubusercontent.com/XIU2/TrackersListCollection/master/all.txt", false, false),
            new("XIU2/TrackersListCollection", "https://raw.githubusercontent.com/XIU2/TrackersListCollection/master/http.txt", false, false),
            new("XIU2/TrackersListCollection", "https://cdn.jsdelivr.net/gh/XIU2/TrackersListCollection/best.txt", true, false),
            new("XIU2/TrackersListCollection", "https://cdn.jsdelivr.net/gh/XIU2/TrackersListCollection/all.txt", true, false),
            new("XIU2/TrackersListCollection", "https://cdn.jsdelivr.net/gh/XIU2/TrackersListCollection/http.txt", true, false)
        };

        var selectedSet = new HashSet<string>(_settingsService.Settings.TrackerSources ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        if (selectedSet.Count == 0)
        {
            selectedSet.Add("https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_best_ip.txt");
            selectedSet.Add("https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_best.txt");
        }

        _settingsService.Settings.CustomTrackerSources ??= new List<string>();
        foreach (var url in _settingsService.Settings.CustomTrackerSources.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var isCdn = url.Contains("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase);
            sources.Add(new TrackerSourceOption("Custom", url.Trim(), isCdn, true));
        }

        foreach (var option in sources)
        {
            option.IsSelected = selectedSet.Contains(option.Url);
            option.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(TrackerSourceOption.IsSelected), StringComparison.Ordinal))
                {
                    RefreshSelectedTrackerSources();
                }
            };
            TrackerSourceOptions.Add(option);
        }

        RefreshSelectedTrackerSources();
    }

    [ObservableProperty]
    private bool _isDefaultClientMagnet = true;

    partial void OnIsDefaultClientMagnetChanged(bool value)
    {
        _settingsService.Settings.DefaultClientMagnet = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isDefaultClientThunder = true;

    partial void OnIsDefaultClientThunderChanged(bool value)
    {
        _settingsService.Settings.DefaultClientThunder = value;
        _settingsService.Save();
    }

    public string Aria2ConfPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downio", "aria2.conf");
    public string Aria2SessionPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downio", "aria2.session");
    public string Aria2LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downio", "aria2.log");

    [RelayCommand]
    public async Task ResetSession()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        var title = GetString("BtnResetSession");
        var message = GetString("MessageConfirmResetSession");
        if (message == "MessageConfirmResetSession") message = "Are you sure you want to reset the download session? This will remove all tasks from the list (but not your files).";

        var dialog = new ConfirmDeleteDialog(title, message, false);
        var result = await dialog.ShowDialog<bool>(mainWindow);
        if (result)
        {
            try
            {
                _refreshTimer.Stop();
                await _aria2Service.ShutdownAsync();
                
                if (File.Exists(Aria2SessionPath))
                {
                    File.Delete(Aria2SessionPath);
                }
                
                await InitializeAria2Async();
                _refreshTimer.Start();
                
                _notificationService.ShowNotification("Session Reset", "Download session has been reset.", ToastType.Success);
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "Failed to reset session");
                _notificationService.ShowNotification("Error", "Failed to reset session.", ToastType.Error);
                _refreshTimer.Start();
            }
        }
    }

    [RelayCommand]
    public async Task ResetSettings()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        var title = GetString("BtnResetSettings");
        var message = GetString("MessageConfirmResetSettings");
        if (message == "MessageConfirmResetSettings") message = "Are you sure you want to reset all settings to defaults?";

        var dialog = new ConfirmDeleteDialog(title, message, false);
        var result = await dialog.ShowDialog<bool>(mainWindow);
        if (result)
        {
            _settingsService.ResetToDefaults();

            var savedTheme = _settingsService.Settings.Theme;
            SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == savedTheme) ?? ThemeOptions.FirstOrDefault(t => t.Value == "System") ?? ThemeOptions[2];

            var savedLang = _settingsService.Settings.Language;
            SelectedLanguage = LanguageOptions.FirstOrDefault(l => l.Value == savedLang) ?? LanguageOptions.FirstOrDefault(l => l.Value == "System") ?? LanguageOptions[2];

            IsAutoStartEnabled = _settingsService.Settings.AutoStart;
            IsAutoInstallUpdatesEnabled = _settingsService.Settings.AutoInstallUpdates;
            IsExitOnClose = _settingsService.Settings.ExitOnClose;

            var savedAccentMode = _settingsService.Settings.AccentMode;
            SelectedAccentMode = AccentModeOptions.FirstOrDefault(a => a.Value == savedAccentMode) ?? AccentModeOptions[0];

            DefaultSavePath = string.IsNullOrWhiteSpace(_settingsService.Settings.DefaultSavePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : _settingsService.Settings.DefaultSavePath;

            ProxyAddress = _settingsService.Settings.ProxyAddress;
            ProxyPort = _settingsService.Settings.ProxyPort;
            ProxyTypeIndex = string.Equals(_settingsService.Settings.ProxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            ProxyUsername = _settingsService.Settings.ProxyUsername;
            ProxyPassword = _settingsService.Settings.ProxyPassword;

            BtTrackers = _settingsService.Settings.BtTrackers;
            RpcPort = _settingsService.Settings.RpcPort;
            RpcSecret = _settingsService.Settings.RpcSecret;
            EnableUpnp = _settingsService.Settings.EnableUpnp;
            BtListenPort = _settingsService.Settings.BtListenPort;
            DhtListenPort = _settingsService.Settings.DhtListenPort;
            GlobalUserAgent = _settingsService.Settings.GlobalUserAgent;
            IsDefaultClientMagnet = _settingsService.Settings.DefaultClientMagnet;
            IsDefaultClientThunder = _settingsService.Settings.DefaultClientThunder;

            ThemeAccentService.Apply(_settingsService.Settings.AccentMode, _settingsService.Settings.CustomAccentColor);
            _notificationService.ShowNotification("Settings Reset", "Settings have been reset to defaults. Please restart the app for some changes to take effect.", ToastType.Info);
        }
    }

    // Settings Properties
    [ObservableProperty]
    private string _defaultSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    [ObservableProperty]
    private string _proxyAddress = string.Empty;

    [ObservableProperty]
    private int _proxyPort = 8080;

    [ObservableProperty]
    private int _proxyTypeIndex; // 0: HTTP, 1: SOCKS5

    public ObservableCollection<string> ProxyTypes { get; } = ["HTTP", "SOCKS5"];

    [ObservableProperty]
    private string _appVersion = "0.0.0";

    public string RepositoryUrl => "https://github.com/pengpercy/Downio";
    public string FeedbackUrl => "https://github.com/pengpercy/Downio/issues/new";

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string _updateCheckStatusText = string.Empty;

    [ObservableProperty]
    private IBrush _updateCheckStatusBrush = Brushes.Gray;

    [ObservableProperty]
    private bool _isUpdateCheckStatusVisible;

    public string CheckUpdateButtonKey => IsCheckingForUpdates ? "BtnCheckingUpdate" : "BtnCheckUpdate";

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckUpdateButtonKey));
    }

    // Theme & Language Options
    public record ThemeOption(string Key, string Value);
    public record LanguageOption(string Key, string Value);
    public record AccentModeOption(string Key, string Value);

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new("ThemeDark", "Dark"),
        new("ThemeLight", "Light"),
        new("ThemeSystem", "System")
    ];

    public ObservableCollection<LanguageOption> LanguageOptions { get; } =
    [
        new("LanguageSystem", "System"),
        new("English", "en-US"),
        new("中文", "zh-CN")
    ];

    public ObservableCollection<AccentModeOption> AccentModeOptions { get; } =
    [
        new("LabelFollowSystem", "System"),
        new("LabelCustomAccent", "Custom")
    ];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private AccentModeOption? _selectedAccentMode;

    [ObservableProperty]
    private bool _isAutoStartEnabled;

    partial void OnIsAutoStartEnabledChanged(bool value)
    {
        _autoStartService.SetAutoStart(value);
        _settingsService.Settings.AutoStart = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isAutoInstallUpdatesEnabled;

    partial void OnIsAutoInstallUpdatesEnabledChanged(bool value)
    {
        _settingsService.Settings.AutoInstallUpdates = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isExitOnClose;

    partial void OnIsExitOnCloseChanged(bool value)
    {
        _settingsService.Settings.ExitOnClose = value;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isAccentFollowSystem = true;

    [ObservableProperty]
    private Color _customAccentColor = Color.Parse("#508252");

    [ObservableProperty]
    private string _customAccentColorHex = "#508252";

    [ObservableProperty]
    private SolidColorBrush _customAccentPreviewBrush = new(Color.Parse("#508252"));

    private bool _isUpdatingAccent;

    partial void OnSelectedAccentModeChanged(AccentModeOption? value)
    {
        if (value == null) return;
        
        var mode = value.Value;
        IsAccentFollowSystem = mode == "System";
        _settingsService.Settings.AccentMode = mode;
        
        if (IsAccentFollowSystem)
        {
            _settingsService.Settings.CustomAccentColor = string.Empty;
        }
        else
        {
            _settingsService.Settings.CustomAccentColor = CustomAccentColorHex;
        }
        
        _settingsService.Save();
        ThemeAccentService.Apply(_settingsService.Settings.AccentMode, _settingsService.Settings.CustomAccentColor);
    }

    partial void OnCustomAccentColorChanged(Color value)
    {
        if (_isUpdatingAccent) return;

        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        
        _isUpdatingAccent = true;
        CustomAccentColorHex = hex;
        CustomAccentPreviewBrush = new SolidColorBrush(value);
        _isUpdatingAccent = false;

        if (IsAccentFollowSystem) return;

        _settingsService.Settings.AccentMode = "Custom";
        _settingsService.Settings.CustomAccentColor = hex;
        _settingsService.Save();
        ThemeAccentService.Apply("Custom", hex);
    }

    partial void OnCustomAccentColorHexChanged(string value)
    {
        if (_isUpdatingAccent) return;

        if (Color.TryParse(value, out var c))
        {
            CustomAccentPreviewBrush = new SolidColorBrush(c);
            _isUpdatingAccent = true;
            CustomAccentColor = c;
            _isUpdatingAccent = false;
        }

        if (IsAccentFollowSystem) return;

        _settingsService.Settings.AccentMode = "Custom";
        _settingsService.Settings.CustomAccentColor = value;
        _settingsService.Save();
        ThemeAccentService.Apply("Custom", value);
    }

    public string EmptyStateSubtitleDownloadingText
    {
        get
        {
            var template = GetString("EmptyStateSubtitleDownloading");
            var shortcut = OperatingSystem.IsMacOS() ? "⌘+N" : "Ctrl+N";

            if (template.Contains("{0}", StringComparison.Ordinal))
            {
                return string.Format(template, shortcut);
            }

            return template.Replace("⌘+N", shortcut).Replace("⌘N", shortcut);
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value != null)
        {
            SetTheme(value.Value);
            _settingsService.Settings.Theme = value.Value;
            _settingsService.Save();
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
        SetLanguage(value.Value);
        
        _settingsService.Settings.Language = value.Value;
        _settingsService.Save();
        
        // Refresh ThemeOptions to trigger converter update for localized strings
        var currentTheme = SelectedTheme;
        var themes = ThemeOptions.ToList();
        ThemeOptions.Clear();
        foreach (var theme in themes)
        {
            ThemeOptions.Add(theme);
        }
        SelectedTheme = currentTheme;

        // Also Refresh LanguageOptions to translate "System" option
        var currentLang = SelectedLanguage;
        var langs = LanguageOptions.ToList();
        LanguageOptions.Clear();
        foreach (var lang in langs)
        {
            LanguageOptions.Add(lang);
        }
        SelectedLanguage = currentLang;

        OnPropertyChanged(nameof(EmptyStateSubtitleDownloadingText));
        OnPropertyChanged(nameof(CheckUpdateButtonKey));
    }

    public MainWindowViewModel()
    {
        _windowControlsOnLeft = DetectWindowControlsOnLeft();
        _settingsService = new SettingsService();
        _autoStartService = new AutoStartService();
        _notificationService = new NotificationService();
        _notificationService.ToastRequested += (s, msg) => ShowToast(msg);
        _aria2Service = new Aria2Service();

        AppVersion = AppVersionProvider.GetCurrentVersion();
        
        // Initialize views
        _taskListView = new TaskListView();
        _settingsView = new SettingsView();
        
        ShowDownloading();
        
        // Initialize selections
        var savedTheme = _settingsService.Settings.Theme;
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == savedTheme) ?? ThemeOptions.FirstOrDefault(t => t.Value == "System") ?? ThemeOptions[2];

        var savedLang = _settingsService.Settings.Language;
        SelectedLanguage = LanguageOptions.FirstOrDefault(l => l.Value == savedLang) ?? LanguageOptions.FirstOrDefault(l => l.Value == "System") ?? LanguageOptions[2];

        IsAutoStartEnabled = _settingsService.Settings.AutoStart;
        IsAutoInstallUpdatesEnabled = _settingsService.Settings.AutoInstallUpdates;
        IsExitOnClose = _settingsService.Settings.ExitOnClose;

        var savedAccentMode = _settingsService.Settings.AccentMode;
        SelectedAccentMode = AccentModeOptions.FirstOrDefault(a => a.Value == savedAccentMode) ?? AccentModeOptions[0];

        if (!string.IsNullOrWhiteSpace(_settingsService.Settings.DefaultSavePath))
        {
            DefaultSavePath = _settingsService.Settings.DefaultSavePath;
        }
        else
        {
            _settingsService.Settings.DefaultSavePath = DefaultSavePath;
            _settingsService.Save();
        }

        ProxyAddress = _settingsService.Settings.ProxyAddress;
        ProxyPort = _settingsService.Settings.ProxyPort;
        ProxyTypeIndex = string.Equals(_settingsService.Settings.ProxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ProxyUsername = _settingsService.Settings.ProxyUsername;
        ProxyPassword = _settingsService.Settings.ProxyPassword;

        // Advanced Settings
        BtTrackers = _settingsService.Settings.BtTrackers;
        RpcPort = _settingsService.Settings.RpcPort;
        RpcSecret = _settingsService.Settings.RpcSecret;
        EnableUpnp = _settingsService.Settings.EnableUpnp;
        BtListenPort = _settingsService.Settings.BtListenPort;
        DhtListenPort = _settingsService.Settings.DhtListenPort;
        GlobalUserAgent = _settingsService.Settings.GlobalUserAgent;
        IsDefaultClientMagnet = _settingsService.Settings.DefaultClientMagnet;
        IsDefaultClientThunder = _settingsService.Settings.DefaultClientThunder;
        AutoSyncTracker = _settingsService.Settings.AutoSyncTracker;
        LastSyncTrackerTime = _settingsService.Settings.LastSyncTrackerTime;
        InitializeTrackerSourceOptions();

        _ = MaybeAutoSyncTrackersAsync();

        IsAccentFollowSystem = !string.Equals(_settingsService.Settings.AccentMode, "Custom", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_settingsService.Settings.CustomAccentColor))
        {
            var hex = _settingsService.Settings.CustomAccentColor;
            if (Color.TryParse(hex, out var c))
            {
                _isUpdatingAccent = true;
                CustomAccentColor = c;
                CustomAccentColorHex = hex;
                CustomAccentPreviewBrush = new SolidColorBrush(c);
                _isUpdatingAccent = false;
            }
        }
        ThemeAccentService.Apply(_settingsService.Settings.AccentMode, _settingsService.Settings.CustomAccentColor);

        // Initialize Aria2 and Timer
        _ = InitializeAria2Async();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshTaskListAsync();
        _refreshTimer.Start();
    }

    private async Task MaybeAutoSyncTrackersAsync()
    {
        try
        {
            if (!AutoSyncTracker) return;
            var last = LastSyncTrackerTime <= 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeMilliseconds(LastSyncTrackerTime);
            if (DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24)) return;

            await SyncTrackersAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private bool DetectWindowControlsOnLeft()
    {
        if (IsMacOS) return true;
        if (!IsLinux) return false;

        try
        {
            var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToUpperInvariant() ?? "";
            
            // Check for KDE Plasma
            if (desktop.Contains("KDE") || desktop.Contains("PLASMA"))
            {
                // Try Plasma 6 first
                var outputKde = TryReadProcessStdOut("kreadconfig6", "--file kwinrc --group org.kde.kwin.decoration --key ButtonsOnLeft");
                if (string.IsNullOrEmpty(outputKde))
                {
                    // Fallback to Plasma 5
                    outputKde = TryReadProcessStdOut("kreadconfig5", "--file kwinrc --group org.kde.kwin.decoration --key ButtonsOnLeft");
                }

                if (!string.IsNullOrWhiteSpace(outputKde))
                {
                    // Check if any of the standard window controls (Close, Maximize, Minimize) are on the left
                    // X: Close, A: Maximize, I: Minimize
                    var layout = outputKde.Trim().ToUpperInvariant();
                    if (layout.Contains('X') || layout.Contains('A') || layout.Contains('I'))
                    {
                        return true;
                    }
                }
                
                // If we are in KDE and detected right-side controls (or empty left side), return false.
                return false;
            }

            // Check for Xfce
            if (desktop.Contains("XFCE"))
            {
                var outputXfce = TryReadProcessStdOut("xfconf-query", "-c xfwm4 -p /general/button_layout");
                if (!string.IsNullOrWhiteSpace(outputXfce))
                {
                    // Format is usually "O|HMC" or "CHM|" where | is title
                    // O: Menu, H: Hide/Min, M: Max, C: Close
                    var layout = outputXfce.Trim().ToUpperInvariant();
                    var parts = layout.Split('|');
                    if (parts.Length > 0)
                    {
                        var leftPart = parts[0];
                        if (leftPart.Contains('C') || leftPart.Contains('M') || leftPart.Contains('H'))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            // Fallback to GNOME/GTK detection
            var output = TryReadProcessStdOut("gsettings", "get org.gnome.desktop.wm.preferences button-layout");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var raw = output.Trim().Trim('\'', '"');
                var parts = raw.Split(':', 2);
                var leftPart = parts.Length > 0 ? parts[0] : string.Empty;
                var rightPart = parts.Length > 1 ? parts[1] : string.Empty;

                static bool HasButton(string s)
                {
                    s = s.ToLowerInvariant();
                    return s.Contains("close") || s.Contains("maximize") || s.Contains("minimize");
                }

                if (HasButton(leftPart) && !HasButton(rightPart)) return true;
                if (!HasButton(leftPart) && HasButton(rightPart)) return false;
                if (HasButton(leftPart)) return true;
            }
        }
        catch
        {
            // Ignore errors, default to false (Windows-like)
        }

        return false;
    }

    private static string? TryReadProcessStdOut(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(300);
            return output;
        }
        catch
        {
            return null;
        }
    }

    // Toast Notifications
    [ObservableProperty]
    private ObservableCollection<ToastMessage> _toasts = new();

    private async void ShowToast(ToastMessage message)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Toasts.Add(message);
            // Auto remove after 3 seconds
            await Task.Delay(3000);
            Toasts.Remove(message);
        });
    }

    private async Task InitializeAria2Async()
    {
        try 
        {
            await _aria2Service.InitializeAsync(_settingsService.Settings);
            _ = ApplyProxySettingsAsync();
            await RefreshTaskListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Aria2 Init Failed: {ex.Message}");
            AppLog.Error(ex, "Aria2 Init Failed");
        }
    }

    [ObservableProperty]
    private string _proxyUsername = string.Empty;

    [ObservableProperty]
    private string _proxyPassword = string.Empty;

    partial void OnDefaultSavePathChanged(string value)
    {
        _settingsService.Settings.DefaultSavePath = value;
        _settingsService.Save();
    }

    partial void OnProxyAddressChanged(string value)
    {
        _settingsService.Settings.ProxyAddress = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyPortChanged(int value)
    {
        _settingsService.Settings.ProxyPort = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyTypeIndexChanged(int value)
    {
        _settingsService.Settings.ProxyType = value == 1 ? "SOCKS5" : "HTTP";
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyUsernameChanged(string value)
    {
        _settingsService.Settings.ProxyUsername = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    partial void OnProxyPasswordChanged(string value)
    {
        _settingsService.Settings.ProxyPassword = value;
        _settingsService.Save();
        _ = ApplyProxySettingsAsync();
    }

    private async Task ApplyProxySettingsAsync()
    {
        try
        {
            var address = ProxyAddress?.Trim() ?? string.Empty;
            var port = ProxyPort;
            var type = ProxyTypeIndex == 1 ? "SOCKS5" : "HTTP";
            var user = ProxyUsername?.Trim() ?? string.Empty;
            var pass = ProxyPassword ?? string.Empty;

            await _aria2Service.ApplyProxyAsync(type, address, port, user, pass);
        }
        catch
        {
        }
    }

    private async Task RefreshTaskListAsync(bool allowRecovery = true)
    {
        try
        {
            var allTasks = await _aria2Service.GetGlobalStatusAsync();

            foreach (var t in allTasks)
            {
                if (_lastStatusByGid.TryGetValue(t.Id, out var prev))
                {
                    if (prev != "StatusError" && t.Status == "StatusError")
                    {
                        AppLog.Warn($"Download failed: {t.Name} ({t.Id})");
                        _notificationService.ShowNotification(GetString("NotificationDownloadFailed"), t.Name, ToastType.Error);
                    }
                    else if (prev != "StatusCompleted" && t.Status == "StatusCompleted")
                    {
                        _notificationService.ShowNotification(GetString("NotificationDownloadComplete"), t.Name, ToastType.Success);
                    }
                }

                _lastStatusByGid[t.Id] = t.Status;
            }

            var activeIds = new HashSet<string>(allTasks.Select(t => t.Id));
            var idsToRemove = _lastStatusByGid.Keys.Where(id => !activeIds.Contains(id)).ToList();
            foreach (var id in idsToRemove)
            {
                _lastStatusByGid.Remove(id);
            }
            
            // Filter based on current view
            var filteredTasks = allTasks.Where(t => 
            {
                if (IsDownloading) return t.Status == "StatusDownloading" || t.Status == "StatusWaiting" || t.Status == "StatusPaused";
                if (IsWaiting) return t.Status == "StatusWaiting";
                if (IsStopped) return t.Status == "StatusStopped" || t.Status == "StatusError" || t.Status == "StatusCompleted";
                return true;
            }).ToList();

            // Sync list
            // 1. Remove missing
            for (int i = Tasks.Count - 1; i >= 0; i--)
            {
                var existing = Tasks[i];
                if (filteredTasks.All(t => t.Id != existing.Id))
                {
                    Tasks.RemoveAt(i);
                }
            }

            // 2. Add or Update
            foreach (var task in filteredTasks)
            {
                var existing = Tasks.FirstOrDefault(t => t.Id == task.Id);
                if (existing == null)
                {
                    Tasks.Add(task);
                }
                else
                {
                    // Update properties
                    if (existing.Status != task.Status) existing.Status = task.Status;
                    if (existing.Progress != task.Progress) existing.Progress = task.Progress;
                    if (existing.DownloadedBytes != task.DownloadedBytes) existing.DownloadedBytes = task.DownloadedBytes;
                    if (existing.TotalBytes != task.TotalBytes) existing.TotalBytes = task.TotalBytes;
                    if (existing.Speed != task.Speed) existing.Speed = task.Speed;
                    if (existing.TimeLeft != task.TimeLeft) existing.TimeLeft = task.TimeLeft;
                    if (existing.Connections != task.Connections) existing.Connections = task.Connections;
                    if (existing.Split != task.Split) existing.Split = task.Split;
                    if (existing.Name != task.Name && task.Name != "Unknown") existing.Name = task.Name;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Refresh Failed: {ex.Message}");
            AppLog.Error(ex, "Refresh task list failed");

            if (allowRecovery && IsConnectionRefused(ex))
            {
                var recovered = await TryRecoverAria2Async().ConfigureAwait(false);
                if (recovered)
                {
                    await RefreshTaskListAsync(allowRecovery: false).ConfigureAwait(false);
                }
            }
        }
    }

    private static bool IsConnectionRefused(Exception ex)
    {
        if (ex is System.Net.Http.HttpRequestException hre)
        {
            if (hre.InnerException is System.Net.Sockets.SocketException se &&
                se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
            {
                return true;
            }
        }

        return ex.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryRecoverAria2Async()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAria2RecoveryAttempt < TimeSpan.FromSeconds(5)) return false;
        if (!await _aria2RecoveryLock.WaitAsync(0).ConfigureAwait(false)) return false;

        try
        {
            _lastAria2RecoveryAttempt = now;
            await _aria2Service.ShutdownAsync().ConfigureAwait(false);
            await _aria2Service.InitializeAsync(_settingsService.Settings).ConfigureAwait(false);
            _ = ApplyProxySettingsAsync();
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Aria2 recovery failed");
            return false;
        }
        finally
        {
            _aria2RecoveryLock.Release();
        }
    }
}
