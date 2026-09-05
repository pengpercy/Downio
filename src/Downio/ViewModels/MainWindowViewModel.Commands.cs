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
using Downio.Services.Aria2;
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
    public void ShowEd2kSearch()
    {
        IsSettingsVisible = true;
        CurrentTitleKey = "MenuEd2kSearch";
        CurrentView = _ed2kSearchView;
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
    public async Task ShowAddTask()
    {
        ShouldFocusNewTaskUrlOnOpen = false;
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
            await TryAutofillNewTaskFromClipboardAsync(mainWindow.Clipboard);
            var dialog = new AddTaskWindow(this);
            await dialog.ShowDialog(mainWindow);
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
        if (UpdateWindowService.ActivateExisting()) return;
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
        Exception? updateError = null;
        try
        {
            release = await updateService.CheckForUpdatesAsync(currentVersion);
        }
        catch (Exception ex)
        {
            updateError = ex;
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
            await UpdateWindowService.ShowAsync(release, _settingsService, mainWindow);
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
                var message = updateError is not null
                    ? $"{GetString("MessageUpdateCheckFailed")} {updateError.Message}"
                    : GetString("MessageNoUpdates");
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

    private async Task TryAutofillNewTaskFromClipboardAsync(IClipboard? clipboard)
    {
        if (clipboard == null) return;

        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            var links = ExtractSupportedDownloadLinks(text)
                .Where(link => !_autoFilledClipboardLinks.Contains(link))
                .ToList();

            if (links.Count == 0) return;

            NewTaskInputModeIndex = 0;
            NewTaskUrl = string.Join(Environment.NewLine, links);
            ShouldFocusNewTaskUrlOnOpen = true;

            foreach (var link in links)
            {
                _autoFilledClipboardLinks.Add(link);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to autofill add task from clipboard");
        }
    }

    private static List<string> ExtractSupportedDownloadLinks(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanupClipboardLinkCandidate)
            .Where(IsSupportedDownloadLink)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanupClipboardLinkCandidate(string value)
    {
        return value.Trim().Trim('"', '\'', '<', '>', '(', ')', '[', ']', '{', '}', ',', ';');
    }

    private static bool IsSupportedDownloadLink(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith("thunder://", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith("ed2k://", StringComparison.OrdinalIgnoreCase)) return true;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme switch
               {
                   "http" => true,
                   "https" => true,
                   "ftp" => true,
                   "ftps" => true,
                   "sftp" => true,
                   _ => false
               };
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
                        displayName = TryGetEd2kFileName(url);
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            displayName = TryGetSafeFileNameFromUri(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null);
                        }
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
        var taskProxy = extraOptions != null && extraOptions.TryGetValue("all-proxy", out var tp) ? tp : null;

        return ProxyEnvironment.CreateHttpHandler(
            _settingsService.Settings.ProxyType,
            _settingsService.Settings.ProxyAddress,
            _settingsService.Settings.ProxyPort,
            _settingsService.Settings.ProxyUsername,
            _settingsService.Settings.ProxyPassword,
            taskProxy);
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

    private static string TryGetEd2kFileName(string value)
    {
        const string prefix = "ed2k://|file|";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return string.Empty;

        var end = value.IndexOf('|', prefix.Length);
        if (end <= prefix.Length) return string.Empty;
        return SanitizeDownloadFileName(value[prefix.Length..end]);
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
        LocalizationService.SwitchLanguage(lang);
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

        var removeSucceeded = true;
        try
        {
            await _aria2Service.RemoveAsync(task.Id);
        }
        catch (Exception ex)
        {
            removeSucceeded = false;
            AppLog.Error(ex, $"Failed to remove task: {task.Name} ({task.Id})");
        }

        _stoppedTaskHistoryService.Remove(task.Id);

        var fileDeleteSucceeded = !dialog.DeleteFile ||
            await TryDeleteTaskFilesAsync(task);

        await RefreshTaskListAsync();
        if (!removeSucceeded)
        {
            _notificationService.ShowNotification(GetString("StatusError"), GetString("MessageTaskDeleteFailed"), ToastType.Error);
        }
        else if (!fileDeleteSucceeded)
        {
            _notificationService.ShowNotification(GetString("StatusError"), GetString("MessageFileDeleteFailed"), ToastType.Error);
        }
        else
        {
            var message = task.Name + (dialog.DeleteFile ? GetString("NotificationAlsoDeletedFile") : string.Empty);
            _notificationService.ShowNotification(GetString("NotificationTaskDeleted"), message, ToastType.Success);
        }
    }

    [RelayCommand]
    public void QuitApp()
    {
        if (Application.Current is App app)
        {
            app.RequestExplicitExit();
            return;
        }

        RequestQuit();
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
        _ed2kSearchCancellation?.Cancel();
        _taskbarBadgeService.Dispose();

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
