using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Downio.Helpers;
using Downio.Models;
using Downio.Services;

namespace Downio.ViewModels;

public sealed record Ed2kFileTypeOption(string ResourceKey, string Value);
public sealed record Ed2kSyncIntervalOption(string ResourceKey, int Hours);
public sealed record Ed2kSortOption(string ResourceKey, string Value);

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

    public ResettableObservableCollection<Ed2kSearchResult> Ed2kSearchResults { get; } = new();

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
        IsEd2kCompactView = settings.Ed2kCompactView;
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
        ClearEd2kResults();
        HasEd2kSearched = true;
        IsEd2kSearching = true;
        Ed2kSearchProgress = 0;
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

            var started = DateTimeOffset.UtcNow;
            var deadline = started.AddSeconds(timeoutSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                var payload = await _aria2Service.GetEd2kSearchResultsAsync(gid);
                MergeEd2kResults(payload.Results);
                Ed2kSearchProgress = Math.Clamp((DateTimeOffset.UtcNow - started).TotalSeconds / timeoutSeconds, 0, 1);
                SetEd2kSearchStatus("Ed2kStatusSearchingCount", Ed2kTotalResultCount);
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            if (Ed2kTotalResultCount == 0)
                SetEd2kSearchStatus("Ed2kStatusNoResults");
            else
                SetEd2kSearchStatus("Ed2kStatusCompleted", Ed2kTotalResultCount);
        }
        catch (OperationCanceledException)
        {
            SetEd2kSearchStatus(Ed2kTotalResultCount == 0 ? "Ed2kStatusCancelled" : "Ed2kStatusStopped", Ed2kTotalResultCount);
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
            Ed2kSearchProgress = 1;
            IsEd2kSearching = false;
            NotifyEd2kResultState();
        }
    }

    [RelayCommand]
    private void CancelEd2kSearch()
    {
        _ed2kSearchCancellation?.Cancel();
    }

    [RelayCommand]
    private void ClearEd2kKeyword()
    {
        Ed2kSearchKeyword = string.Empty;
    }

    // ---- Result list -------------------------------------------------------------------------
    // Every result of the current search, keyed by hash. Instances are reused across polls so the
    // visible rows update in place instead of being torn down and rebuilt every second.
    private readonly Dictionary<string, Ed2kSearchResult> _ed2kResultIndex = new(StringComparer.OrdinalIgnoreCase);

    // Links queued during this session, so "Added" survives a new search for the same file.
    private readonly HashSet<string> _ed2kAddedLinks = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<Ed2kSortOption> Ed2kSortOptions { get; } =
    [
        new("Ed2kSortBySources", "sources"),
        new("Ed2kSortBySize", "size"),
        new("Ed2kSortByName", "name")
    ];

    [ObservableProperty]
    private Ed2kSortOption? _selectedEd2kSort;

    partial void OnSelectedEd2kSortChanged(Ed2kSortOption? value)
    {
        OnPropertyChanged(nameof(Ed2kSortValue));
        ApplyEd2kResultView();
    }

    /// <summary>Lets the table header mark the column currently sorted on.</summary>
    public string Ed2kSortValue => SelectedEd2kSort?.Value ?? "sources";

    /// <summary>Dense table rows instead of cards.</summary>
    [ObservableProperty]
    private bool _isEd2kCompactView = true;

    partial void OnIsEd2kCompactViewChanged(bool value) =>
        SaveEd2kImmediateSetting(() => _settingsService.Settings.Ed2kCompactView = value);

    [RelayCommand]
    private void ShowEd2kTable() => IsEd2kCompactView = true;

    [RelayCommand]
    private void ShowEd2kCards() => IsEd2kCompactView = false;

    // Table column headers sort by their column.
    [RelayCommand]
    private void SortEd2kBy(string value) =>
        SelectedEd2kSort = Ed2kSortOptions.FirstOrDefault(o => o.Value == value) ?? SelectedEd2kSort;

    partial void OnSelectedEd2kFileTypeChanged(Ed2kFileTypeOption? value) => ApplyEd2kResultView();

    [ObservableProperty]
    private bool _hasEd2kSearched;

    [ObservableProperty]
    private double _ed2kSearchProgress;

    public int Ed2kTotalResultCount => _ed2kResultIndex.Count;

    public bool HasEd2kResults => Ed2kSearchResults.Count > 0;

    /// <summary>The search found files, but none match the selected type chip.</summary>
    public bool IsEd2kFilteredEmpty => _ed2kResultIndex.Count > 0 && Ed2kSearchResults.Count == 0;

    /// <summary>A finished search with nothing at all to show.</summary>
    public bool IsEd2kNoResults => HasEd2kSearched && !IsEd2kSearching && _ed2kResultIndex.Count == 0;

    public bool IsEd2kWelcome => !HasEd2kSearched;

    /// <summary>Searching, but nothing has come back yet.</summary>
    public bool IsEd2kWaitingForResults => IsEd2kSearching && _ed2kResultIndex.Count == 0;

    partial void OnIsEd2kSearchingChanged(bool value) => NotifyEd2kResultState();

    partial void OnHasEd2kSearchedChanged(bool value) => NotifyEd2kResultState();

    // Kept as a running count: recounting on every IsSelected change made "Select all" quadratic.
    private int _ed2kSelectedCount;
    private bool _isBatchSelectingEd2k;

    public int Ed2kSelectedCount => _ed2kSelectedCount;

    public bool HasEd2kSelection => Ed2kSelectedCount > 0;

    public string Ed2kSelectionText => string.Format(CultureInfo.CurrentCulture, GetString("Ed2kSelectedCount"), Ed2kSelectedCount);

    private void ClearEd2kResults()
    {
        foreach (var result in _ed2kResultIndex.Values) result.PropertyChanged -= OnEd2kResultPropertyChanged;
        _ed2kResultIndex.Clear();
        _ed2kSelectedCount = 0;
        Ed2kSearchResults.Clear();
        NotifyEd2kResultState();
    }

    private void MergeEd2kResults(IEnumerable<Ed2kSearchResult> incoming)
    {
        foreach (var result in incoming)
        {
            var key = result.Key;
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (_ed2kResultIndex.TryGetValue(key, out var existing))
            {
                existing.UpdateCountsFrom(result);
                continue;
            }

            result.IsAdded = _ed2kAddedLinks.Contains(result.Ed2kLink);
            result.PropertyChanged += OnEd2kResultPropertyChanged;
            _ed2kResultIndex[key] = result;
        }

        ApplyEd2kResultView();
    }

    private void ApplyEd2kResultView()
    {
        var type = SelectedEd2kFileType?.Value ?? string.Empty;
        var query = _ed2kResultIndex.Values.Where(r => type.Length == 0 || r.FileTypeCategory == type);
        var ordered = (SelectedEd2kSort?.Value) switch
        {
            "size" => query.OrderByDescending(r => r.SizeBytes),
            "name" => query.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query.OrderByDescending(r => r.SourceCountValue).ThenByDescending(r => r.CompleteSourceCountValue)
        };
        var desired = ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        // Hidden rows drop out of a batch so "Download selected" only acts on what is visible.
        RunEd2kSelectionBatch(() =>
        {
            foreach (var hidden in Ed2kSearchResults.Except(desired)) hidden.IsSelected = false;
        });

        var misplaced = 0;
        for (var i = 0; i < desired.Count && misplaced <= Ed2kIncrementalMoveLimit; i++)
        {
            if (i >= Ed2kSearchResults.Count || !ReferenceEquals(Ed2kSearchResults[i], desired[i])) misplaced++;
        }

        if (misplaced == 0 && Ed2kSearchResults.Count == desired.Count)
        {
            // Nothing moved: counts update in place through the rows' own bindings.
        }
        else if (misplaced > Ed2kIncrementalMoveLimit)
        {
            // A big reshuffle (every poll re-sorts by sources): one Reset and the virtualizing panel
            // re-realizes only the visible rows, instead of n Move notifications with O(n) lookups each.
            Ed2kSearchResults.ResetTo(desired);
        }
        else
        {
            // Small changes move in place, which keeps hover and focus on untouched rows.
            for (var i = 0; i < desired.Count; i++)
            {
                var item = desired[i];
                if (i < Ed2kSearchResults.Count && ReferenceEquals(Ed2kSearchResults[i], item)) continue;
                var oldIndex = Ed2kSearchResults.IndexOf(item);
                if (oldIndex >= 0) Ed2kSearchResults.Move(oldIndex, i);
                else Ed2kSearchResults.Insert(i, item);
            }
            while (Ed2kSearchResults.Count > desired.Count) Ed2kSearchResults.RemoveAt(Ed2kSearchResults.Count - 1);
        }

        NotifyEd2kResultState();
    }

    private const int Ed2kIncrementalMoveLimit = 24;

    private void OnEd2kResultPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Ed2kSearchResult.IsSelected) || sender is not Ed2kSearchResult result) return;
        _ed2kSelectedCount += result.IsSelected ? 1 : -1;
        if (!_isBatchSelectingEd2k) NotifyEd2kSelection();
    }

    private void RunEd2kSelectionBatch(Action change)
    {
        var before = _ed2kSelectedCount;
        _isBatchSelectingEd2k = true;
        try
        {
            change();
        }
        finally
        {
            _isBatchSelectingEd2k = false;
        }
        if (_ed2kSelectedCount != before) NotifyEd2kSelection();
    }

    private void NotifyEd2kResultState()
    {
        OnPropertyChanged(nameof(Ed2kTotalResultCount));
        OnPropertyChanged(nameof(HasEd2kResults));
        OnPropertyChanged(nameof(IsEd2kFilteredEmpty));
        OnPropertyChanged(nameof(IsEd2kNoResults));
        OnPropertyChanged(nameof(IsEd2kWelcome));
        OnPropertyChanged(nameof(IsEd2kWaitingForResults));
        NotifyEd2kSelection();
    }

    private void NotifyEd2kSelection()
    {
        OnPropertyChanged(nameof(Ed2kSelectedCount));
        OnPropertyChanged(nameof(HasEd2kSelection));
        OnPropertyChanged(nameof(Ed2kSelectionText));
    }

    [RelayCommand]
    private void SelectAllEd2kResults()
    {
        RunEd2kSelectionBatch(() =>
        {
            foreach (var result in Ed2kSearchResults.Where(r => !r.IsAdded)) result.IsSelected = true;
        });
    }

    [RelayCommand]
    private void ClearEd2kSelection()
    {
        RunEd2kSelectionBatch(() =>
        {
            foreach (var result in Ed2kSearchResults) result.IsSelected = false;
        });
    }

    [RelayCommand]
    private async Task CopyEd2kLink(Ed2kSearchResult? result)
    {
        if (result == null || string.IsNullOrWhiteSpace(result.Ed2kLink)) return;
        if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard }) return;

        try
        {
            var data = new Avalonia.Input.DataTransfer();
            data.Add(Avalonia.Input.DataTransferItem.CreateText(result.Ed2kLink));
            await clipboard.SetDataAsync(data);
            _notificationService.ShowNotification(GetString("NotificationCopied"), GetString("MessageLinkCopied"), ToastType.Success);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Copy ED2K link failed: {result.Name}");
        }
    }

    // ---- Downloads ---------------------------------------------------------------------------

    [RelayCommand]
    private async Task DownloadEd2kResult(Ed2kSearchResult? result)
    {
        if (result == null || result.IsAdded) return;

        if (await TryAddEd2kDownloadAsync(result))
        {
            _notificationService.ShowNotification(
                GetString("Ed2kDownloadStartedTitle"),
                string.Format(GetString("Ed2kDownloadStartedMessage"), result.Name),
                ToastType.Success);
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedEd2kResults()
    {
        var selected = Ed2kSearchResults.Where(r => r.IsSelected && !r.IsAdded).ToList();
        if (selected.Count == 0) return;

        var added = 0;
        foreach (var result in selected)
        {
            if (await TryAddEd2kDownloadAsync(result)) added++;
        }

        if (added > 0)
        {
            _notificationService.ShowNotification(
                GetString("Ed2kDownloadStartedTitle"),
                string.Format(GetString("Ed2kDownloadsStartedMessage"), added),
                ToastType.Success);
        }
    }

    private async Task<bool> TryAddEd2kDownloadAsync(Ed2kSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Ed2kLink)) return false;

        try
        {
            var fileName = SanitizeDownloadFileName(result.Name);
            var gid = await _aria2Service.AddUriAsync(
                result.Ed2kLink,
                fileName,
                DefaultSavePath,
                DefaultDownloadSplit);
            if (string.IsNullOrWhiteSpace(gid)) throw new InvalidOperationException("aria2 did not return a download GID.");

            _ed2kAddedLinks.Add(result.Ed2kLink);
            result.IsAdded = true;
            result.IsSelected = false;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Failed to download ED2K result: {result.Ed2kLink}");
            _notificationService.ShowNotification(GetString("Ed2kDownloadFailedTitle"), $"{result.Name}: {ex.Message}", ToastType.Error);
            return false;
        }
    }
}
