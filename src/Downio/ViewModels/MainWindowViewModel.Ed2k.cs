using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downio.Models;
using Downio.Services;

namespace Downio.ViewModels;

public sealed record Ed2kFileTypeOption(string ResourceKey, string Value);
public sealed record Ed2kSyncIntervalOption(string ResourceKey, int Hours);

public partial class MainWindowViewModel
{
    public ObservableCollection<Ed2kFileTypeOption> Ed2kFileTypeOptions { get; } =
    [
        new("Ed2kTypeAny", ""),
        new("Ed2kTypeAudio", "audio"),
        new("Ed2kTypeVideo", "video"),
        new("Ed2kTypeDocument", "doc"),
        new("Ed2kTypeArchive", "archive")
    ];

    public ObservableCollection<Ed2kSearchResult> Ed2kSearchResults { get; } = new();

    public ObservableCollection<Ed2kSyncIntervalOption> Ed2kSyncIntervalOptions { get; } =
    [
        new("Ed2kIntervalEveryStartup", 0),
        new("Ed2kInterval6Hours", 6),
        new("Ed2kInterval12Hours", 12),
        new("Ed2kIntervalDaily", 24),
        new("Ed2kIntervalWeekly", 168)
    ];

    [ObservableProperty]
    private string _ed2kSearchKeyword = string.Empty;

    [ObservableProperty]
    private Ed2kFileTypeOption? _selectedEd2kFileType;

    [ObservableProperty]
    private int _ed2kMinSourceCount;

    partial void OnEd2kMinSourceCountChanged(int value)
    {
        if (_suppressEd2kSettingsChanges) return;
        var normalized = Math.Clamp(value, 0, 999999);
        if (normalized != value)
        {
            Ed2kMinSourceCount = normalized;
            return;
        }
        _settingsService.Settings.Ed2kMinSourceCount = normalized;
        _settingsService.Save();
    }

    [ObservableProperty]
    private int _ed2kSearchTimeoutSeconds = 20;

    partial void OnEd2kSearchTimeoutSecondsChanged(int value)
    {
        if (_suppressEd2kSettingsChanges) return;
        var normalized = Math.Clamp(value, 10, 600);
        if (normalized != value)
        {
            Ed2kSearchTimeoutSeconds = normalized;
            return;
        }
        _settingsService.Settings.Ed2kSearchTimeout = normalized;
        _settingsService.Save();
    }

    [ObservableProperty]
    private bool _isEd2kSearching;

    [ObservableProperty]
    private string _ed2kSearchStatus = string.Empty;

    private string _ed2kSearchStatusResourceKey = string.Empty;
    private object[] _ed2kSearchStatusArguments = [];

    private void SetEd2kSearchStatus(string resourceKey, params object[] arguments)
    {
        _ed2kSearchStatusResourceKey = resourceKey;
        _ed2kSearchStatusArguments = arguments;
        RefreshEd2kSearchStatusText();
    }

    private void RefreshEd2kSearchStatusText()
    {
        if (string.IsNullOrWhiteSpace(_ed2kSearchStatusResourceKey)) return;
        var template = GetString(_ed2kSearchStatusResourceKey);
        Ed2kSearchStatus = _ed2kSearchStatusArguments.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, _ed2kSearchStatusArguments);
    }

    private CancellationTokenSource? _ed2kSearchCancellation;

    private bool _suppressEd2kSettingsChanges = true;

    [ObservableProperty]
    private int _ed2kListenPort = 4662;

    partial void OnEd2kListenPortChanged(int value) => SaveEd2kEngineSetting(() => _settingsService.Settings.Ed2kListenPort = value);

    [ObservableProperty]
    private int _ed2kUdpListenPort = 4672;

    partial void OnEd2kUdpListenPortChanged(int value) => SaveEd2kEngineSetting(() => _settingsService.Settings.Ed2kUdpListenPort = value);

    [ObservableProperty]
    private int _ed2kUploadSlots = 3;

    partial void OnEd2kUploadSlotsChanged(int value) => SaveEd2kEngineSetting(() => _settingsService.Settings.Ed2kUploadSlots = value);

    [ObservableProperty]
    private string _ed2kServer = string.Empty;

    partial void OnEd2kServerChanged(string value) => SaveEd2kEngineSetting(() => _settingsService.Settings.Ed2kServer = value);

    [ObservableProperty]
    private string _ed2kServerMetUrl = "https://upd.emule-security.org/server.met";

    partial void OnEd2kServerMetUrlChanged(string value) => SaveEd2kImmediateSetting(() => _settingsService.Settings.Ed2kServerMetUrl = value);

    [ObservableProperty]
    private string _ed2kNodesDatUrl = "https://upd.emule-security.org/nodes.dat";

    partial void OnEd2kNodesDatUrlChanged(string value) => SaveEd2kImmediateSetting(() => _settingsService.Settings.Ed2kNodesDatUrl = value);

    [ObservableProperty]
    private bool _ed2kBootstrapAutoSync = true;

    partial void OnEd2kBootstrapAutoSyncChanged(bool value) => SaveEd2kImmediateSetting(() => _settingsService.Settings.Ed2kBootstrapAutoSync = value);

    [ObservableProperty]
    private Ed2kSyncIntervalOption? _selectedEd2kSyncInterval;

    partial void OnSelectedEd2kSyncIntervalChanged(Ed2kSyncIntervalOption? value) =>
        SaveEd2kImmediateSetting(() => _settingsService.Settings.Ed2kBootstrapSyncIntervalHours = value?.Hours ?? 24);

    [ObservableProperty]
    private bool _ed2kSettingsRestartRequired;

    [ObservableProperty]
    private bool _isApplyingEd2kSettings;

    [ObservableProperty]
    private bool _isSyncingEd2kBootstrap;

    public string Ed2kBootstrapLastSyncText
    {
        get
        {
            var timestamp = GetEd2kBootstrapLastModifiedMillis();
            return timestamp <= 0
                ? GetString("Ed2kBootstrapNotSynced")
                : DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }
    }

    private void LoadEd2kSettings()
    {
        _suppressEd2kSettingsChanges = true;
        var settings = _settingsService.Settings;
        Ed2kListenPort = Math.Clamp(settings.Ed2kListenPort, 0, 65535);
        Ed2kUdpListenPort = Math.Clamp(settings.Ed2kUdpListenPort, 0, 65535);
        Ed2kUploadSlots = Math.Clamp(settings.Ed2kUploadSlots, 1, 100);
        Ed2kServer = settings.Ed2kServer ?? string.Empty;
        Ed2kServerMetUrl = string.IsNullOrWhiteSpace(settings.Ed2kServerMetUrl)
            ? "https://upd.emule-security.org/server.met"
            : settings.Ed2kServerMetUrl;
        Ed2kNodesDatUrl = string.IsNullOrWhiteSpace(settings.Ed2kNodesDatUrl)
            ? "https://upd.emule-security.org/nodes.dat"
            : settings.Ed2kNodesDatUrl;
        Ed2kBootstrapAutoSync = settings.Ed2kBootstrapAutoSync;
        SelectedEd2kSyncInterval = Ed2kSyncIntervalOptions.FirstOrDefault(x => x.Hours == settings.Ed2kBootstrapSyncIntervalHours)
                                   ?? Ed2kSyncIntervalOptions.First(x => x.Hours == 24);
        Ed2kMinSourceCount = Math.Clamp(settings.Ed2kMinSourceCount, 0, 999999);
        Ed2kSearchTimeoutSeconds = Math.Clamp(settings.Ed2kSearchTimeout, 10, 600);
        Ed2kSettingsRestartRequired = false;
        _suppressEd2kSettingsChanges = false;
        OnPropertyChanged(nameof(Ed2kBootstrapLastSyncText));
    }

    private void SaveEd2kEngineSetting(Action update)
    {
        if (_suppressEd2kSettingsChanges) return;
        update();
        _settingsService.Save();
        Ed2kSettingsRestartRequired = true;
    }

    private void SaveEd2kImmediateSetting(Action update)
    {
        if (_suppressEd2kSettingsChanges) return;
        update();
        _settingsService.Save();
    }

    [RelayCommand]
    private async Task ApplyEd2kSettings()
    {
        var validationKey = ValidateEd2kSettings();
        if (validationKey != null)
        {
            _notificationService.ShowNotification(GetString("SettingsEd2k"), GetString(validationKey), ToastType.Warning);
            return;
        }

        IsApplyingEd2kSettings = true;
        _refreshTimer.Stop();
        _ed2kSearchCancellation?.Cancel();
        try
        {
            Ed2kServer = NormalizeEd2kServerLines(Ed2kServer);
            Ed2kServerMetUrl = Ed2kServerMetUrl.Trim();
            Ed2kNodesDatUrl = Ed2kNodesDatUrl.Trim();
            await _aria2Service.ShutdownAsync();
            await _aria2Service.InitializeAsync(_settingsService.Settings);
            await ApplyProxySettingsAsync();
            Ed2kSettingsRestartRequired = false;
            await RefreshTaskListAsync(allowRecovery: false);
            _notificationService.ShowNotification(GetString("SettingsEd2k"), GetString("Ed2kSettingsApplied"), ToastType.Success);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to apply ED2K settings");
            _notificationService.ShowNotification(GetString("SettingsEd2k"), $"{GetString("Ed2kSettingsApplyFailed")} {ex.Message}", ToastType.Error);
        }
        finally
        {
            _refreshTimer.Start();
            IsApplyingEd2kSettings = false;
        }
    }

    private string? ValidateEd2kSettings()
    {
        if (Ed2kListenPort is < 0 or > 65535 || Ed2kUdpListenPort is < 0 or > 65535)
            return "Ed2kInvalidListenPort";
        if (Ed2kUploadSlots is < 1 or > 100) return "Ed2kInvalidUploadSlots";
        if (Ed2kMinSourceCount is < 0 or > 999999) return "Ed2kInvalidMinSources";
        if (Ed2kSearchTimeoutSeconds is < 10 or > 600) return "Ed2kInvalidSearchTimeout";
        if (!ValidateEd2kServerLines(Ed2kServer)) return "Ed2kInvalidServer";
        if (!IsHttpUrl(Ed2kServerMetUrl) || !IsHttpUrl(Ed2kNodesDatUrl)) return "Ed2kInvalidBootstrapUrl";
        return null;
    }

    private static bool ValidateEd2kServerLines(string value)
    {
        return SplitEd2kServerLines(value).All(line =>
        {
            var separator = line.LastIndexOf(':');
            return separator > 0 && separator < line.Length - 1 &&
                   int.TryParse(line[(separator + 1)..], out var port) && port is > 0 and <= 65535;
        });
    }

    private static string NormalizeEd2kServerLines(string value) => string.Join(Environment.NewLine, SplitEd2kServerLines(value));

    private static string[] SplitEd2kServerLines(string value) => value
        .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [RelayCommand]
    private async Task SyncEd2kBootstrap()
    {
        await SyncEd2kBootstrapCoreAsync(showNotification: true);
    }

    private async Task MaybeAutoSyncEd2kBootstrapAsync()
    {
        if (!Ed2kBootstrapAutoSync || IsSyncingEd2kBootstrap) return;
        var lastSync = GetEd2kBootstrapLastModifiedMillis();
        var intervalHours = SelectedEd2kSyncInterval?.Hours ?? 24;
        if (intervalHours > 0 && lastSync > 0 &&
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastSync < TimeSpan.FromHours(intervalHours).TotalMilliseconds)
        {
            return;
        }
        await SyncEd2kBootstrapCoreAsync(showNotification: false);
    }

    private async Task<bool> SyncEd2kBootstrapCoreAsync(bool showNotification)
    {
        if (IsSyncingEd2kBootstrap) return false;
        if (!IsHttpUrl(Ed2kServerMetUrl) || !IsHttpUrl(Ed2kNodesDatUrl))
        {
            if (showNotification)
                _notificationService.ShowNotification(GetString("SettingsEd2k"), GetString("Ed2kInvalidBootstrapUrl"), ToastType.Warning);
            return false;
        }

        IsSyncingEd2kBootstrap = true;
        try
        {
            using var handler = CreateDownloadProbeHandler(null);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            var serverTask = DownloadEd2kBootstrapFileAsync(client, Ed2kServerMetUrl);
            var nodesTask = DownloadEd2kBootstrapFileAsync(client, Ed2kNodesDatUrl);
            await Task.WhenAll(serverTask, nodesTask);

            var directory = GetEd2kBootstrapDirectory();
            Directory.CreateDirectory(directory);
            var serverPath = Path.Combine(directory, "server.met");
            var nodesPath = Path.Combine(directory, "nodes.dat");
            var serverTemp = serverPath + ".tmp";
            var nodesTemp = nodesPath + ".tmp";
            await File.WriteAllBytesAsync(serverTemp, await serverTask);
            await File.WriteAllBytesAsync(nodesTemp, await nodesTask);
            File.Move(serverTemp, serverPath, overwrite: true);
            File.Move(nodesTemp, nodesPath, overwrite: true);
            OnPropertyChanged(nameof(Ed2kBootstrapLastSyncText));

            if (showNotification)
                _notificationService.ShowNotification(GetString("SettingsEd2k"), GetString("Ed2kBootstrapSyncSucceeded"), ToastType.Success);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to sync ED2K bootstrap files");
            if (showNotification)
                _notificationService.ShowNotification(GetString("SettingsEd2k"), $"{GetString("Ed2kBootstrapSyncFailed")} {ex.Message}", ToastType.Error);
            return false;
        }
        finally
        {
            IsSyncingEd2kBootstrap = false;
        }
    }

    private static async Task<byte[]> DownloadEd2kBootstrapFileAsync(HttpClient client, string url)
    {
        const int maxSize = 16 * 1024 * 1024;
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is <= 0 or > maxSize)
            throw new InvalidDataException("Invalid ED2K bootstrap file size.");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length is 0 or > maxSize) throw new InvalidDataException("Invalid ED2K bootstrap file size.");
        return bytes;
    }

    private static string GetEd2kBootstrapDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downio", "ed2k");

    private static long GetEd2kBootstrapLastModifiedMillis()
    {
        var directory = GetEd2kBootstrapDirectory();
        return new[] { Path.Combine(directory, "server.met"), Path.Combine(directory, "nodes.dat") }
            .Where(File.Exists)
            .Select(path => new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds())
            .DefaultIfEmpty(0)
            .Max();
    }

    [RelayCommand]
    private async Task StartEd2kSearch()
    {
        if (IsEd2kSearching) return;
        var keyword = Ed2kSearchKeyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            SetEd2kSearchStatus("Ed2kKeywordRequired");
            return;
        }

        var timeoutSeconds = Math.Clamp(Ed2kSearchTimeoutSeconds, 10, 600);
        Ed2kSearchTimeoutSeconds = timeoutSeconds;
        Ed2kSearchResults.Clear();
        IsEd2kSearching = true;
        SetEd2kSearchStatus("Ed2kStatusSearching");
        _ed2kSearchCancellation = new CancellationTokenSource();
        var token = _ed2kSearchCancellation.Token;
        var gid = string.Empty;

        try
        {
            gid = await _aria2Service.StartEd2kSearchAsync(
                keyword,
                SelectedEd2kFileType?.Value ?? string.Empty,
                Math.Max(0, Ed2kMinSourceCount));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                var payload = await _aria2Service.GetEd2kSearchResultsAsync(gid);
                var ordered = payload.Results
                    .OrderByDescending(x => int.TryParse(x.SourceCount, out var count) ? count : 0)
                    .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                Ed2kSearchResults.Clear();
                foreach (var result in ordered) Ed2kSearchResults.Add(result);
                SetEd2kSearchStatus("Ed2kStatusSearchingCount", Ed2kSearchResults.Count);
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            if (Ed2kSearchResults.Count == 0)
                SetEd2kSearchStatus("Ed2kStatusNoResults");
            else
                SetEd2kSearchStatus("Ed2kStatusCompleted", Ed2kSearchResults.Count);
        }
        catch (OperationCanceledException)
        {
            SetEd2kSearchStatus("Ed2kStatusCancelled");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "ED2K search failed");
            SetEd2kSearchStatus("Ed2kStatusFailed", ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(gid)) await _aria2Service.CleanupEd2kSearchAsync(gid);
            _ed2kSearchCancellation?.Dispose();
            _ed2kSearchCancellation = null;
            IsEd2kSearching = false;
        }
    }

    [RelayCommand]
    private void CancelEd2kSearch()
    {
        _ed2kSearchCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task DownloadEd2kResult(Ed2kSearchResult? result)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.Ed2kLink)) return;

        try
        {
            var fileName = SanitizeDownloadFileName(result.Name);
            var gid = await _aria2Service.AddUriAsync(
                result.Ed2kLink,
                fileName,
                DefaultSavePath,
                DefaultDownloadSplit);
            if (string.IsNullOrWhiteSpace(gid)) throw new InvalidOperationException("aria2 did not return a download GID.");

            _notificationService.ShowNotification(
                GetString("Ed2kDownloadStartedTitle"),
                string.Format(GetString("Ed2kDownloadStartedMessage"), string.IsNullOrWhiteSpace(fileName) ? result.Name : fileName),
                ToastType.Success);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Failed to download ED2K result: {result.Ed2kLink}");
            _notificationService.ShowNotification(GetString("Ed2kDownloadFailedTitle"), ex.Message, ToastType.Error);
        }
    }
}
