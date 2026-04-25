using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downio.Models;
using Downio.Services;
using Downio.Views;

namespace Downio.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    public void OpenRepoUrl()
    {
        OpenUrl(RepositoryUrl);
    }

    [RelayCommand]
    public void OpenFeedbackUrl()
    {
        OpenUrl(FeedbackUrl);
    }

    [RelayCommand]
    public void OpenExternalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        OpenUrl(url);
    }

    [RelayCommand]
    public async Task CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;
            var clipboard = mainWindow.Clipboard;
            if (clipboard == null) return;

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(data);
            _notificationService.ShowNotification(GetString("NotificationCopied"), GetString("MessageLinkCopied"), ToastType.Success);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "CopyText failed");
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenUrl Failed: {ex.Message}");
            AppLog.Error(ex, $"OpenUrl failed for {url}");
        }
    }

    [RelayCommand]
    public void TogglePane()
    {
        IsPaneOpen = !IsPaneOpen;
    }

    [RelayCommand]
    public void ShowDownloading()
    {
        IsSettingsVisible = false;
        CurrentTitleKey = "MenuDownloading";
        CurrentView = _taskListView;
        _ = RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowWaiting()
    {
        IsSettingsVisible = false;
        CurrentTitleKey = "MenuWaiting";
        CurrentView = _taskListView;
        _ = RefreshTaskListAsync();
    }

    [RelayCommand]
    public void ShowStopped()
    {
        IsSettingsVisible = false;
        CurrentTitleKey = "MenuStopped";
        CurrentView = _taskListView;
        _ = RefreshTaskListAsync();
    }

    [RelayCommand]
    public async Task ShowSettings()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        SelectedSettingsSection = SettingsSection.General;

        var window = new SettingsWindow
        {
            DataContext = this
        };

        await window.ShowDialog(mainWindow);
    }

    [RelayCommand]
    public void SelectSettingsSection(SettingsSection section)
    {
        SelectedSettingsSection = section;
    }

    [RelayCommand]
    public void ShowAddTask()
    {
        NewTaskInputModeIndex = 0;
        NewTaskUrl = string.Empty;
        NewTaskTorrentFilePath = string.Empty;
        NewTaskName = string.Empty;
        NewTaskChunks = DefaultDownloadSplit;
        if (string.IsNullOrEmpty(NewTaskSavePath))
        {
            NewTaskSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var dialog = new AddTaskWindow(this);
            dialog.ShowDialog(mainWindow);
        }
    }

    [RelayCommand]
    public void CancelAddTask()
    {
        IsAddTaskVisible = false;
        NewTaskUrl = string.Empty;
        NewTaskName = string.Empty;
    }

    [RelayCommand]
    public async Task ChooseSavePath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Download Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                NewTaskSavePath = folders[0].Path.LocalPath;
            }
        }
    }

    [RelayCommand]
    public async Task ChooseDefaultSavePath()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Default Download Folder",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                DefaultSavePath = folders[0].Path.LocalPath;
            }
        }
    }

    [RelayCommand]
    public async Task CheckForUpdates(object? parameter)
    {
        if (IsCheckingForUpdates) return;
        
        var isFromAbout = parameter?.ToString() == "About";
        
        if (isFromAbout)
        {
            IsUpdateCheckStatusVisible = false;
        }

        IsCheckingForUpdates = true;
        var updateService = new UpdateService(_settingsService.Settings);
        var currentVersion = AppVersion.TrimStart('v');

        ReleaseInfo? release = null;
        try
        {
            release = await updateService.CheckForUpdatesAsync(currentVersion);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            AppLog.Error(ex, "Update check failed");

            if (isFromAbout)
            {
                UpdateCheckStatusText = $"{GetString("MessageUpdateCheckFailed")} {ex.Message}";
                UpdateCheckStatusBrush = Brushes.Red;
                IsUpdateCheckStatusVisible = true;
                _ = Task.Delay(8000).ContinueWith(_ => IsUpdateCheckStatusVisible = false, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        if (release != null)
        {
            var dialog = new UpdateWindow(release, _settingsService);
            await dialog.ShowDialog(mainWindow);
        }
        else
        {
            if (isFromAbout)
            {
                if (UpdateCheckStatusBrush == Brushes.Red && !string.IsNullOrWhiteSpace(UpdateCheckStatusText))
                {
                    IsUpdateCheckStatusVisible = true;
                }
                else
                {
                    UpdateCheckStatusText = GetString("MessageNoUpdates");
                    UpdateCheckStatusBrush = Brushes.Gray;
                    IsUpdateCheckStatusVisible = true;
                }
                
                // Hide after 5 seconds
                _ = Task.Delay(5000).ContinueWith(_ => IsUpdateCheckStatusVisible = false, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                var title = GetString("TitleUpdateCheck");
                var message = GetString("MessageNoUpdates");
                var dialog = new InfoDialog(title, message);
                await dialog.ShowDialog(mainWindow);
            }
        }
    }

    private static string GetString(string key)
    {
        if (Application.Current != null && Application.Current.TryGetResource(key, null, out var resource) && resource is string str)
        {
            return str;
        }
        return key;
    }

    [RelayCommand]
    public async Task StartDownload()
    {
        bool isTorrent = NewTaskInputModeIndex == 1;
        if (!isTorrent && string.IsNullOrWhiteSpace(NewTaskUrl)) return;
        if (isTorrent && string.IsNullOrWhiteSpace(NewTaskTorrentFilePath)) return;

        try
        {
            IDictionary<string, string>? extraOptions = null;
            if (NewTaskShowAdvanced)
            {
                var options = new Dictionary<string, string>();
                
                if (!string.IsNullOrWhiteSpace(NewTaskUserAgent))
                {
                    options["user-agent"] = NewTaskUserAgent.Trim();
                }
                
                if (!string.IsNullOrWhiteSpace(NewTaskReferer))
                {
                    options["referer"] = NewTaskReferer.Trim();
                }
                
                var headers = new List<string>();
                if (!string.IsNullOrWhiteSpace(NewTaskAuthorization))
                {
                    headers.Add($"Authorization: {NewTaskAuthorization.Trim()}");
                }
                if (!string.IsNullOrWhiteSpace(NewTaskCookie))
                {
                    headers.Add($"Cookie: {NewTaskCookie.Trim()}");
                }
                if (headers.Count > 0)
                {
                    options["header"] = string.Join("\n", headers);
                }
                
                if (!string.IsNullOrWhiteSpace(NewTaskProxy))
                {
                    options["all-proxy"] = NewTaskProxy.Trim();
                }
                
                if (options.Count > 0)
                {
                    extraOptions = options;
                }
            }

            if (isTorrent)
            {
                var gid = await _aria2Service.AddTorrentAsync(NewTaskTorrentFilePath, NewTaskSavePath, extraOptions);
                AddPendingTask(gid, Path.GetFileNameWithoutExtension(NewTaskTorrentFilePath), NewTaskSavePath, string.Empty, NewTaskChunks);
            }
            else
            {
                // Support multiple links (one per line)
                var urls = NewTaskUrl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(u => u.Trim())
                                     .Where(u => !string.IsNullOrWhiteSpace(u))
                                     .ToList();

                foreach (var url in urls)
                {
                    var outputName = urls.Count == 1 ? NewTaskName?.Trim() ?? string.Empty : string.Empty;
                    var displayName = outputName;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = TryGetSafeFileNameFromUri(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null);
                    }

                    var gid = await _aria2Service.AddUriAsync(url, outputName, NewTaskSavePath, NewTaskChunks, extraOptions);
                    AddPendingTask(gid, displayName, NewTaskSavePath, url, NewTaskChunks);
                }
            }

            IsAddTaskVisible = false;
            NewTaskUrl = string.Empty;
            NewTaskTorrentFilePath = string.Empty;
            NewTaskName = string.Empty;
            NewTaskShowAdvanced = false;
            NewTaskUserAgent = string.Empty;
            NewTaskAuthorization = string.Empty;
            NewTaskReferer = string.Empty;
            NewTaskCookie = string.Empty;
            NewTaskProxy = string.Empty;

            if (CurrentTitleKey != "MenuDownloading")
            {
                ShowDownloading();
            }

            _ = RefreshTaskListSoonAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Add Task Failed: {ex.Message}");
            AppLog.Error(ex, "Add task failed");
        }
    }

    private void AddPendingTask(string gid, string name, string savePath, string url, int split)
    {
        if (string.IsNullOrWhiteSpace(gid)) return;
        if (!IsDownloading) return;
        if (Tasks.Any(t => t.Id == gid)) return;

        var displayName = string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        Tasks.Add(new DownloadTask
        {
            Id = gid,
            Name = displayName,
            Status = "StatusWaiting",
            Speed = "0 B/s",
            TimeLeft = "--",
            Split = Math.Clamp(split, 1, 32),
            Connections = 0,
            Url = url,
            FilePath = string.IsNullOrWhiteSpace(name) ? string.Empty : Path.Combine(savePath, name)
        });
    }

    private async Task RefreshTaskListSoonAsync()
    {
        await Task.Delay(800);
        await RefreshTaskListAsync();
    }

    private async Task<string> TryDetectDownloadFileNameAsync(string url, IDictionary<string, string>? extraOptions)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        try
        {
            using var handler = CreateDownloadProbeHandler(extraOptions);
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };

            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            ApplyDownloadProbeHeaders(head, extraOptions);

            using var headResponse = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var fromHead = TryGetFileNameFromResponse(headResponse);
            if (!string.IsNullOrWhiteSpace(fromHead))
            {
                return fromHead;
            }

            using var get = new HttpRequestMessage(HttpMethod.Get, uri);
            get.Headers.Range = new RangeHeaderValue(0, 0);
            ApplyDownloadProbeHeaders(get, extraOptions);

            using var getResponse = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var fromGet = TryGetFileNameFromResponse(getResponse);
            if (!string.IsNullOrWhiteSpace(fromGet))
            {
                return fromGet;
            }

            return TryGetSafeFileNameFromUri(getResponse.RequestMessage?.RequestUri);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to detect download filename for {url}: {ex.Message}");
            return string.Empty;
        }
    }

    private HttpClientHandler CreateDownloadProbeHandler(IDictionary<string, string>? extraOptions)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true
        };

        var proxy = extraOptions != null && extraOptions.TryGetValue("all-proxy", out var taskProxy)
            ? taskProxy
            : string.Empty;

        if (string.IsNullOrWhiteSpace(proxy))
        {
            var address = _settingsService.Settings.ProxyAddress?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(address) && _settingsService.Settings.ProxyPort > 0)
            {
                var scheme = string.Equals(_settingsService.Settings.ProxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
                proxy = $"{scheme}://{address}:{_settingsService.Settings.ProxyPort}";
            }
        }

        if (!string.IsNullOrWhiteSpace(proxy) && Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri))
        {
            var webProxy = new WebProxy(proxyUri);
            var user = _settingsService.Settings.ProxyUsername?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(user))
            {
                webProxy.Credentials = new NetworkCredential(user, _settingsService.Settings.ProxyPassword ?? string.Empty);
            }

            handler.UseProxy = true;
            handler.Proxy = webProxy;
        }

        return handler;
    }

    private void ApplyDownloadProbeHeaders(HttpRequestMessage request, IDictionary<string, string>? extraOptions)
    {
        var userAgent = extraOptions != null && extraOptions.TryGetValue("user-agent", out var ua) && !string.IsNullOrWhiteSpace(ua)
            ? ua
            : "Downio/1.0";
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

        if (extraOptions == null) return;

        if (extraOptions.TryGetValue("referer", out var referer) && !string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", referer);
        }

        if (extraOptions.TryGetValue("header", out var rawHeaders) && !string.IsNullOrWhiteSpace(rawHeaders))
        {
            foreach (var line in rawHeaders.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0) continue;

                var name = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }
    }

    private static string TryGetFileNameFromResponse(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentDisposition != null)
        {
            var name = response.Content.Headers.ContentDisposition.FileNameStar;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = response.Content.Headers.ContentDisposition.FileName;
            }

            var cleaned = SanitizeDownloadFileName(name);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        if (response.Content.Headers.TryGetValues("Content-Disposition", out var values))
        {
            foreach (var value in values)
            {
                var parsed = TryParseContentDispositionFileName(value);
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    return parsed;
                }
            }
        }

        return TryGetSafeFileNameFromUri(response.RequestMessage?.RequestUri);
    }

    private static string TryParseContentDispositionFileName(string value)
    {
        const string fileNameStar = "filename*=";
        var starIndex = value.IndexOf(fileNameStar, StringComparison.OrdinalIgnoreCase);
        if (starIndex >= 0)
        {
            var raw = ReadDispositionValue(value[(starIndex + fileNameStar.Length)..]);
            var decoded = DecodeRfc5987FileName(raw);
            var cleaned = SanitizeDownloadFileName(decoded);
            if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
        }

        const string fileName = "filename=";
        var index = value.IndexOf(fileName, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var raw = ReadDispositionValue(value[(index + fileName.Length)..]);
            return SanitizeDownloadFileName(raw);
        }

        return string.Empty;
    }

    private static string ReadDispositionValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed.Trim('"');
        }

        var separator = trimmed.IndexOf(';');
        return separator >= 0 ? trimmed[..separator].Trim() : trimmed.Trim();
    }

    private static string DecodeRfc5987FileName(string value)
    {
        var parts = value.Split('\'', 3);
        if (parts.Length == 3)
        {
            try
            {
                return Uri.UnescapeDataString(parts[2]);
            }
            catch
            {
                return parts[2];
            }
        }

        return Uri.UnescapeDataString(value);
    }

    private static string TryGetSafeFileNameFromUri(Uri? uri)
    {
        if (uri == null) return string.Empty;

        var name = SanitizeDownloadFileName(Path.GetFileName(uri.LocalPath));
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Avoid forcing generic endpoint names like /download?id=... onto aria2.
        return Path.HasExtension(name) ? name : string.Empty;
    }

    private static string SanitizeDownloadFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        var decoded = WebUtility.UrlDecode(fileName.Trim().Trim('"'));
        decoded = decoded.Replace('/', '_').Replace('\\', '_');

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(decoded.Length);
        foreach (var ch in decoded)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString().Trim().Trim('.');
    }

    [RelayCommand]
    public void SetTheme(string theme)
    {
        var app = Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = theme switch
            {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default
            };
        }
    }

    [RelayCommand]
    public void SetLanguage(string lang)
    {
        if (lang == "System")
        {
            var culture = CultureInfo.CurrentCulture;
            LocalizationService.SwitchLanguage(culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-CN"
                : "en-US");
        }
        else
        {
            LocalizationService.SwitchLanguage(lang);
        }
    }

    [RelayCommand]
    public async Task PauseAll()
    {
        try
        {
            foreach (var task in Tasks)
            {
                if (task.Status == "StatusDownloading" || task.Status == "StatusWaiting")
                {
                    task.Status = "StatusPaused";
                    task.Speed = "0 B/s";
                }
            }

            _refreshTimer.Stop();

            await _aria2Service.PauseAllAsync();

            await Task.Delay(500);
            await RefreshTaskListAsync();

            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PauseAll Failed: {ex.Message}");
            AppLog.Error(ex, "Pause all failed");
            _refreshTimer.Start();
            await RefreshTaskListAsync();
        }
    }

    [RelayCommand]
    public async Task ResumeAll()
    {
        try
        {
            foreach (var task in Tasks)
            {
                if (task.Status == "StatusPaused")
                {
                    task.Status = "StatusWaiting";
                }
            }

            _refreshTimer.Stop();

            await _aria2Service.UnpauseAllAsync();

            await Task.Delay(500);
            await RefreshTaskListAsync();

            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ResumeAll Failed: {ex.Message}");
            AppLog.Error(ex, "Resume all failed");
            _refreshTimer.Start();
            await RefreshTaskListAsync();
        }
    }

    [RelayCommand]
    public async Task DeleteTask(DownloadTask? task)
    {
        if (task == null) return;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }) return;

        var dialog = new ConfirmDeleteDialog(task.Name);
        var result = await dialog.ShowDialog<bool>(mainWindow);

        if (!result) return;

        await _aria2Service.RemoveAsync(task.Id);

        if (dialog.DeleteFile && !string.IsNullOrEmpty(task.FilePath))
        {
            try
            {
                if (File.Exists(task.FilePath))
                {
                    File.Delete(task.FilePath);
                }
                var aria2File = task.FilePath + ".aria2";
                if (File.Exists(aria2File))
                {
                    File.Delete(aria2File);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete file: {ex.Message}");
                AppLog.Error(ex, $"Failed to delete file for task: {task.Name} ({task.Id})");
            }
        }

        await RefreshTaskListAsync();
        var message = task.Name + (dialog.DeleteFile ? GetString("NotificationAlsoDeletedFile") : string.Empty);
        _notificationService.ShowNotification(GetString("NotificationTaskDeleted"), message, ToastType.Success);
    }

    [RelayCommand]
    public void QuitApp()
    {
        _ = ShutdownServicesAsync();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }


    public async Task ShutdownServicesAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        try
        {
            await Task.Run(() => _aria2Service.ShutdownAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Shutdown services failed");
        }
    }
}
